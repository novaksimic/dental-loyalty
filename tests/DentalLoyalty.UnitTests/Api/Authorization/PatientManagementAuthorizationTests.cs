using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// Role authorization (not tenant isolation - see TenantIsolationTests) for the
// id-based patient management endpoints, which are ClinicAdmin-only
// (PatientsController: PlatformAdmin is deliberately excluded, SECURITY.md
// §5.1/§7.12; Patients use GET /api/patients/me instead). Every rejected call
// targets a real, existing patient and verifies nothing was changed, so a 401/403
// here proves the request was stopped before reaching the handler.
public class PatientManagementAuthorizationTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public PatientManagementAuthorizationTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static HttpRequestMessage BuildRequest(string method, Guid patientId)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/patients/{patientId}");

        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { phoneNumber = "+19995550000", email = "changed@example.com" });
        }

        return request;
    }

    private async Task AssertPatientUnchanged(Guid patientId)
    {
        var patient = await TestDataSeeder.GetPatientAsync(_factory, patientId);
        Assert.NotNull(patient);
        Assert.Equal("+15550000", patient!.PhoneNumber);
        Assert.Null(patient.Email);
        Assert.True(patient.IsActive);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task IdBasedEndpoint_WithoutToken_Returns401_AndDoesNotModifyData(string method)
    {
        var (_, patientId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        var response = await _factory.CreateClient().SendAsync(BuildRequest(method, patientId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertPatientUnchanged(patientId);
    }

    [Theory]
    [InlineData("GET", UserRole.Patient)]
    [InlineData("PUT", UserRole.Patient)]
    [InlineData("DELETE", UserRole.Patient)]
    [InlineData("GET", UserRole.PlatformAdmin)]
    [InlineData("PUT", UserRole.PlatformAdmin)]
    [InlineData("DELETE", UserRole.PlatformAdmin)]
    public async Task IdBasedEndpoint_AsNonClinicAdminRole_Returns403_AndDoesNotModifyData(string method, UserRole role)
    {
        var (clinicId, patientId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        // Patient tokens carry the same clinic as the target patient, so only the
        // role (not the tenant) is what's being rejected. PlatformAdmin has no clinic.
        var token = TestJwt.CreateValidToken(role, role == UserRole.PlatformAdmin ? null : clinicId);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(BuildRequest(method, patientId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertPatientUnchanged(patientId);
    }

    [Fact]
    public async Task Create_AsPlatformAdmin_CreatesPatientInExplicitlyRequestedClinic()
    {
        // PlatformAdmin is the one caller with no tenant of its own, so the
        // body's ClinicId is honoured (CreatePatientHandler).
        var (clinicId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateValidToken(UserRole.PlatformAdmin));

        var response = await client.PostAsJsonAsync("/api/patients", new
        {
            clinicId,
            firstName = "Paula",
            lastName = "Platform",
            phoneNumber = "+15551110000",
            email = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedPatientResponse>();
        var created = await TestDataSeeder.GetPatientAsync(_factory, body!.Id);
        Assert.Equal(clinicId, created!.ClinicId);

        var account = Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, body.Id));
        Assert.Equal(clinicId, account.ClinicId);
        Assert.Equal(0, account.Points);
    }

    // ADR-006 through the real pipeline: the account is created with the Patient,
    // in the ClinicAdmin's own clinic regardless of the body's ClinicId, and no
    // loyalty cycle starts before the patient registers (BUSINESS.md §5.1).
    [Fact]
    public async Task Create_AsClinicAdmin_CreatesZeroBalanceAccountInOwnClinic_AndNoCycle()
    {
        var (ownClinicId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");
        var (otherClinicId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic B", "Bob");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateValidToken(UserRole.ClinicAdmin, ownClinicId));

        var response = await client.PostAsJsonAsync("/api/patients", new
        {
            clinicId = otherClinicId,
            firstName = "Clara",
            lastName = "Clinic",
            phoneNumber = "+15552220000",
            email = (string?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedPatientResponse>();
        var account = Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, body!.Id));
        Assert.Equal(ownClinicId, account.ClinicId);
        Assert.Equal(0, account.Points);
        Assert.Empty(await TestDataSeeder.GetLoyaltyCyclesAsync(_factory, body.Id));
    }

    private sealed record CreatedPatientResponse(Guid Id);
}
