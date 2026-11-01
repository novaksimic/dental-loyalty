using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// GET/PUT /api/loyalty-configuration: ClinicAdmin only (SECURITY.md §5.2/§5.4
// "Loyalty Configuration" = Own Clinic; Patients may not change it, §5.3;
// PlatformAdmin only provisions the default on clinic creation, §5.1). The
// endpoints take no clinic id, so the tenant claim alone decides which clinic's
// configuration is read or changed. Every rejected call verifies nothing changed.
public class LoyaltyConfigurationEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string Endpoint = "/api/loyalty-configuration";

    private readonly DentalLoyaltyApiFactory _factory;

    public LoyaltyConfigurationEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient ClinicAdminClient(Guid clinicId) =>
        ClientWithToken(TestJwt.CreateValidToken(UserRole.ClinicAdmin, clinicId));

    private static HttpRequestMessage BuildRequest(string method, string url = Endpoint)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { spendAmountRsd = 1m, pointsAwarded = 99 });
        }

        return request;
    }

    private async Task AssertConfiguration(Guid clinicId, decimal spendAmountRsd, int pointsAwarded)
    {
        var configuration = await TestDataSeeder.GetLoyaltyConfigurationAsync(_factory, clinicId);
        Assert.NotNull(configuration);
        Assert.Equal(spendAmountRsd, configuration!.SpendAmountRsd);
        Assert.Equal(pointsAwarded, configuration.PointsAwarded);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task WithoutToken_Returns401_AndDoesNotModifyData(string method)
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);

        var response = await _factory.CreateClient().SendAsync(BuildRequest(method));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertConfiguration(clinicId, 100m, 1_000);
    }

    [Theory]
    [InlineData("GET", UserRole.Patient)]
    [InlineData("PUT", UserRole.Patient)]
    [InlineData("GET", UserRole.PlatformAdmin)]
    [InlineData("PUT", UserRole.PlatformAdmin)]
    public async Task AsNonClinicAdminRole_Returns403_AndDoesNotModifyData(string method, UserRole role)
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);

        // A Patient token carries the same clinic, so only the role is wrong.
        // PlatformAdmin tokens never carry a clinic.
        var token = role == UserRole.PlatformAdmin
            ? TestJwt.CreateValidToken(role)
            : TestJwt.CreateValidToken(role, clinicId);

        var response = await ClientWithToken(token).SendAsync(BuildRequest(method));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertConfiguration(clinicId, 100m, 1_000);
    }

    [Fact]
    public async Task Get_AsClinicAdmin_ReturnsOwnClinicsConfigurationOnly()
    {
        var clinicAId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 50m, 250);

        var response = await ClinicAdminClient(clinicAId).GetAsync(Endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(clinicAId, body.GetProperty("clinicId").GetGuid());
        Assert.Equal(100m, body.GetProperty("spendAmountRsd").GetDecimal());
        Assert.Equal(1_000, body.GetProperty("pointsAwarded").GetInt32());
    }

    [Fact]
    public async Task Put_AsClinicAdmin_ChangesOwnClinicOnly()
    {
        var clinicAId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        var clinicBId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 50m, 250);

        var response = await ClinicAdminClient(clinicAId).PutAsJsonAsync(
            Endpoint,
            new { spendAmountRsd = 200.50m, pointsAwarded = 3_000 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertConfiguration(clinicAId, 200.50m, 3_000);
        await AssertConfiguration(clinicBId, 50m, 250);
    }

    // Attacker model: a valid ClinicAdmin token for Clinic A plus Clinic B's id,
    // supplied every way a client could - query string and request body. The
    // endpoints have no clinic parameter, so neither is ever read.
    [Fact]
    public async Task Get_WithAnotherClinicsIdInQuery_StillReturnsOwnClinic()
    {
        var clinicAId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        var clinicBId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 50m, 250);

        var response = await ClinicAdminClient(clinicAId).GetAsync($"{Endpoint}?clinicId={clinicBId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(clinicAId, body.GetProperty("clinicId").GetGuid());
        Assert.Equal(1_000, body.GetProperty("pointsAwarded").GetInt32());
    }

    [Fact]
    public async Task Put_WithAnotherClinicsIdInQueryAndBody_ChangesOwnClinicOnly()
    {
        var clinicAId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        var clinicBId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 50m, 250);

        var response = await ClinicAdminClient(clinicAId).PutAsJsonAsync(
            $"{Endpoint}?clinicId={clinicBId}",
            new { clinicId = clinicBId, spendAmountRsd = 1m, pointsAwarded = 99 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertConfiguration(clinicBId, 50m, 250);
        await AssertConfiguration(clinicAId, 1m, 99);
    }

    [Theory]
    [InlineData("0", 1_000)]
    [InlineData("-100", 1_000)]
    [InlineData("100.001", 1_000)]
    [InlineData("100", 0)]
    [InlineData("100", -5)]
    public async Task Put_WithInvalidRatio_Returns400_AndDoesNotModifyData(string spendAmountRsd, int pointsAwarded)
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);

        var response = await ClinicAdminClient(clinicId).PutAsJsonAsync(
            Endpoint,
            new { spendAmountRsd = decimal.Parse(spendAmountRsd), pointsAwarded });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertConfiguration(clinicId, 100m, 1_000);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task ClinicAdmin_ForClinicWithoutConfiguration_Returns404(string method)
    {
        var response = await ClinicAdminClient(Guid.NewGuid()).SendAsync(BuildRequest(method));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A ClinicAdmin token the real issuer would never produce: no clinic claim.
    // With no tenant there is no configuration to resolve.
    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task ClinicAdmin_WithoutClinicClaim_Returns404(string method)
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);

        var token = TestJwt.CreateToken(new[]
        {
            new Claim(JwtClaimNames.UserId, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Role, nameof(UserRole.ClinicAdmin)),
        });

        var response = await ClientWithToken(token).SendAsync(BuildRequest(method));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertConfiguration(clinicId, 100m, 1_000);
    }

    // SECURITY.md §5.1: PlatformAdmin provisions the default configuration when
    // it creates a clinic (BUSINESS.md §3.1: 100 RSD = 1,000 points).
    [Fact]
    public async Task CreateClinic_AsPlatformAdmin_ProvisionsDefaultConfiguration()
    {
        var response = await ClientWithToken(TestJwt.CreateValidToken(UserRole.PlatformAdmin))
            .PostAsJsonAsync("/api/clinics", new { name = "New Clinic" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var clinicId = body.GetProperty("id").GetGuid();

        await AssertConfiguration(clinicId, 100m, 1_000);
    }
}
