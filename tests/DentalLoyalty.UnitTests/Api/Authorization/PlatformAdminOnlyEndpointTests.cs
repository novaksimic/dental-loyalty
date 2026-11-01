using System.Net;
using System.Net.Http.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// Exercises role-based authorization against POST /api/clinics, which is
// restricted to PlatformAdmin. Also verifies the "role" claim JwtTokenGenerator
// issues is correctly mapped to ASP.NET Core's Roles-based [Authorize]/
// User.IsInRole() (wired via TokenValidationParameters.RoleClaimType in
// DentalLoyalty.Infrastructure.DependencyInjection) - if that mapping were
// broken, every case here would return 403 regardless of role.
public class PlatformAdminOnlyEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public PlatformAdminOnlyEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static object ValidClinicBody() => new { Name = "Bright Smiles Dental" };

    private HttpClient AuthenticatedClient(UserRole role)
    {
        var client = _factory.CreateClient();
        var token = TestJwt.CreateValidToken(role);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Post_AsPlatformAdmin_Returns201()
    {
        var client = AuthenticatedClient(UserRole.PlatformAdmin);

        var response = await client.PostAsJsonAsync("/api/clinics", ValidClinicBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_AsClinicAdmin_Returns403()
    {
        var client = AuthenticatedClient(UserRole.ClinicAdmin);

        var response = await client.PostAsJsonAsync("/api/clinics", ValidClinicBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AsPatient_Returns403()
    {
        var client = AuthenticatedClient(UserRole.Patient);

        var response = await client.PostAsJsonAsync("/api/clinics", ValidClinicBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/clinics", ValidClinicBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
