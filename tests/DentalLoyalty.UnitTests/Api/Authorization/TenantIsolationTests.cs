using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// Attacker model (per Slice 5 spec): a valid JWT, a valid ClinicAdmin role, and
// knowledge of another tenant's resource id. Authentication + role authorization
// alone (Slice 4) do not stop this attacker - these tests prove the tenant
// boundary itself does.
public class TenantIsolationTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public TenantIsolationTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClinicAdminClient(Guid clinicId)
    {
        var client = _factory.CreateClient();
        var token = TestJwt.CreateValidToken(UserRole.ClinicAdmin, clinicId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Get_OwnTenantPatient_ReturnsOk()
    {
        var (clinicAId, patientAId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        var response = await ClinicAdminClient(clinicAId).GetAsync($"/api/patients/{patientAId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_CrossTenantPatient_Returns404()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");
        var (_, patientBId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic B", "Bob");

        // ClinicAdmin A, holding a genuinely valid token and role, targets
        // Clinic B's patient id directly.
        var response = await ClinicAdminClient(clinicAId).GetAsync($"/api/patients/{patientBId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_CrossTenantPatient_Returns404_AndDoesNotModifyData()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");
        var (_, patientBId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic B", "Bob");

        var response = await ClinicAdminClient(clinicAId).PutAsJsonAsync(
            $"/api/patients/{patientBId}",
            new { phoneNumber = "+19995550000", email = "attacker-set@example.com" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var patientB = await TestDataSeeder.GetPatientAsync(_factory, patientBId);
        Assert.NotNull(patientB);
        Assert.Equal("+15550000", patientB!.PhoneNumber);
        Assert.Null(patientB.Email);
    }

    [Fact]
    public async Task Put_OwnTenantPatient_Returns204_AndModifiesData()
    {
        var (clinicAId, patientAId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        var response = await ClinicAdminClient(clinicAId).PutAsJsonAsync(
            $"/api/patients/{patientAId}",
            new { phoneNumber = "+15551234567", email = "alice@example.com" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var patientA = await TestDataSeeder.GetPatientAsync(_factory, patientAId);
        Assert.Equal("+15551234567", patientA!.PhoneNumber);
        Assert.Equal("alice@example.com", patientA.Email);
    }

    [Fact]
    public async Task Delete_CrossTenantPatient_Returns404_AndDoesNotDeactivate()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");
        var (_, patientBId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic B", "Bob");

        var response = await ClinicAdminClient(clinicAId).DeleteAsync($"/api/patients/{patientBId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var patientB = await TestDataSeeder.GetPatientAsync(_factory, patientBId);
        Assert.True(patientB!.IsActive);
    }

    [Fact]
    public async Task Delete_OwnTenantPatient_Returns204_AndDeactivates()
    {
        var (clinicAId, patientAId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        var response = await ClinicAdminClient(clinicAId).DeleteAsync($"/api/patients/{patientAId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var patientA = await TestDataSeeder.GetPatientAsync(_factory, patientAId);
        Assert.False(patientA!.IsActive);
    }

    [Fact]
    public async Task Post_WithCrossTenantClinicIdInBody_CreatesPatientInAuthenticatedTenant_Not_RequestedOne()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");
        var (clinicBId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic B", "Bob");

        // ClinicAdmin A sends Clinic B's id as the target clinic in the body.
        var response = await ClinicAdminClient(clinicAId).PostAsJsonAsync(
            "/api/patients",
            new
            {
                clinicId = clinicBId,
                firstName = "Eve",
                lastName = "Attacker",
                phoneNumber = "+15559999999",
                email = (string?)null,
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedPatientResponse>();
        var created = await TestDataSeeder.GetPatientAsync(_factory, body!.Id);

        Assert.NotNull(created);
        Assert.Equal(clinicAId, created!.ClinicId);
        Assert.NotEqual(clinicBId, created.ClinicId);
    }

    [Fact]
    public async Task Get_NonexistentPatient_Returns404_SameAsCrossTenant()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        // Must be indistinguishable from Get_CrossTenantPatient_Returns404, so a
        // caller cannot probe whether an id exists in another tenant.
        var response = await ClinicAdminClient(clinicAId).GetAsync($"/api/patients/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task ClinicAdminTokenWithoutTenantClaim_FailsClosed_Returns404_AndDoesNotModifyData(string method)
    {
        var (_, patientId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Alice");

        // Correct role, but no clinicId claim => no tenant context. The real
        // issuer never produces this (User invariant), so it must not widen
        // access to "any tenant" - the tenant-scoped handlers refuse instead.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateValidToken(UserRole.ClinicAdmin, clinicId: null));

        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/patients/{patientId}");
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { phoneNumber = "+19995550000", email = "changed@example.com" });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var patient = await TestDataSeeder.GetPatientAsync(_factory, patientId);
        Assert.Equal("+15550000", patient!.PhoneNumber);
        Assert.True(patient.IsActive);
    }

    private sealed record CreatedPatientResponse(Guid Id);
}
