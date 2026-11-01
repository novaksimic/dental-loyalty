using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// Attacker model: a Patient holding a genuinely valid JWT (valid signature,
// Patient role, real tenant) who wants to read some other patient's profile -
// in the same clinic or another one. GET /api/patients/me accepts no patient id,
// so these tests prove the profile returned is always derived from the
// authenticated UserId + TenantId and nothing the client can supply.
public class PatientSelfAccessTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string MeUrl = "/api/patients/me";

    private readonly DentalLoyaltyApiFactory _factory;

    public PatientSelfAccessTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithToken(UserRole role, Guid? clinicId, Guid? userId = null)
    {
        var client = _factory.CreateClient();
        var token = TestJwt.CreateValidToken(role, clinicId, userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient PatientClient(Guid clinicId, Guid userId) =>
        ClientWithToken(UserRole.Patient, clinicId, userId);

    [Fact]
    public async Task GetMe_PatientWithLinkedRecord_ReturnsOwnProfile()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        var (aliceUserId, alicePatientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Alice");

        var response = await PatientClient(clinicAId, aliceUserId).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<PatientProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal(alicePatientId, profile!.Id);
        Assert.Equal(clinicAId, profile.ClinicId);
        Assert.Equal("Alice", profile.FirstName);
    }

    [Fact]
    public async Task GetMe_SameTenant_NeverReturnsAnotherPatientsProfile()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        var (aliceUserId, alicePatientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Alice");
        var (_, amyPatientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Amy");

        // Alice tries to steer /me at Amy's record via a query-string id; the
        // endpoint has no such parameter and must ignore it.
        var response = await PatientClient(clinicAId, aliceUserId)
            .GetAsync($"{MeUrl}?patientId={amyPatientId}&id={amyPatientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<PatientProfileResponse>();
        Assert.Equal(alicePatientId, profile!.Id);
        Assert.NotEqual(amyPatientId, profile.Id);
    }

    [Fact]
    public async Task GetById_AsPatient_ForAnotherPatient_Returns403()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        var (aliceUserId, _) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Alice");
        var (_, amyPatientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Amy");

        // The id-based endpoint is the obvious alternative route to Amy's data -
        // it is ClinicAdmin-only, so a Patient is rejected before any lookup.
        var response = await PatientClient(clinicAId, aliceUserId).GetAsync($"/api/patients/{amyPatientId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_PatientFromClinicA_NeverReturnsClinicBPatient()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        var (clinicBId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic B", "Unlinked");
        var (aliceUserId, alicePatientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Alice");
        var (_, bobPatientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicBId, "Bob");

        var response = await PatientClient(clinicAId, aliceUserId).GetAsync($"{MeUrl}?patientId={bobPatientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content.ReadFromJsonAsync<PatientProfileResponse>();
        Assert.Equal(alicePatientId, profile!.Id);
        Assert.Equal(clinicAId, profile.ClinicId);
    }

    [Fact]
    public async Task GetMe_UserIdMatchesClinicBPatient_ButTokenTenantIsClinicA_Returns404()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        var (clinicBId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic B", "Unlinked");
        var (bobUserId, _) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicBId, "Bob");

        // A validly signed token whose UserId owns a Clinic B patient but whose
        // tenant is Clinic A (e.g. a stale or mis-issued token). Ownership alone
        // must not cross the tenant boundary.
        var response = await PatientClient(clinicAId, bobUserId).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_AsClinicAdmin_Returns403()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");

        var response = await ClientWithToken(UserRole.ClinicAdmin, clinicAId).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_AsPlatformAdmin_Returns403()
    {
        var response = await ClientWithToken(UserRole.PlatformAdmin, clinicId: null).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_PatientWithoutLinkedRecord_Returns404()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");

        // Valid Patient token for a UserId no Patient record is linked to.
        var response = await PatientClient(clinicAId, Guid.NewGuid()).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_PatientTokenWithoutTenantClaim_Returns404()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        var (aliceUserId, _) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Alice");

        // No clinicId claim => no tenant context => fail closed, even though the
        // UserId does own a Patient record.
        var response = await ClientWithToken(UserRole.Patient, clinicId: null, aliceUserId).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_PatientTokenWithoutUserIdClaim_Returns404()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Alice");

        // Validly signed, Patient role, real tenant - but no userId. No owner
        // => fail closed, never "first patient in the tenant".
        var token = TestJwt.CreateToken(
        [
            new Claim(JwtClaimNames.Role, nameof(UserRole.Patient)),
            new Claim(JwtClaimNames.ClinicId, clinicAId.ToString()),
        ]);

        var response = await ClientWithRawToken(token).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_PatientTokenWithMalformedUserIdClaim_Returns404()
    {
        var (clinicAId, _) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic A", "Unlinked");
        await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicAId, "Alice");

        var token = TestJwt.CreateToken(
        [
            new Claim(JwtClaimNames.UserId, "not-a-guid"),
            new Claim(JwtClaimNames.Role, nameof(UserRole.Patient)),
            new Claim(JwtClaimNames.ClinicId, clinicAId.ToString()),
        ]);

        var response = await ClientWithRawToken(token).GetAsync(MeUrl);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient ClientWithRawToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record PatientProfileResponse(
        Guid Id,
        Guid ClinicId,
        string FirstName,
        string LastName,
        string PhoneNumber,
        string? Email,
        bool IsActive);
}
