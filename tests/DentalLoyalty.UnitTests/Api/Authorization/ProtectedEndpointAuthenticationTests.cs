using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// Exercises JWT bearer authentication itself (independent of role authorization
// and tenant isolation, which have their own test classes) against
// POST /api/patients, a protected endpoint. The request body's ClinicId is a
// throwaway value: since Slice 5, CreatePatientHandler ignores it for a
// ClinicAdmin caller and uses the authenticated tenant instead (see
// TenantIsolationTests), so no prerequisite clinic needs to exist for these
// authentication-focused tests to be meaningful.
public class ProtectedEndpointAuthenticationTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public ProtectedEndpointAuthenticationTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static object ValidPatientBody() => new
    {
        ClinicId = Guid.NewGuid(),
        FirstName = "Ada",
        LastName = "Lovelace",
        PhoneNumber = "+1-555-0100",
        Email = (string?)null,
    };

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/patients", ValidPatientBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithMalformedToken_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.PostAsJsonAsync("/api/patients", ValidPatientBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithExpiredToken_Returns401()
    {
        var client = _factory.CreateClient();
        var token = TestJwt.CreateExpiredToken(UserRole.ClinicAdmin);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/patients", ValidPatientBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithValidTokenForPermittedRole_Succeeds()
    {
        var client = _factory.CreateClient();
        // A real ClinicAdmin token always carries a ClinicId claim (User domain
        // invariant, enforced at issuance by LoginHandler/JwtTokenGenerator).
        var token = TestJwt.CreateValidToken(UserRole.ClinicAdmin, Guid.NewGuid());
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/patients", ValidPatientBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithValidTokenForDisallowedRole_Returns403()
    {
        // Patient is authenticated but is not an appropriate role to manage
        // patient records (SECURITY.md §5.4: patient management is an
        // admin-level operation).
        var client = _factory.CreateClient();
        var token = TestJwt.CreateValidToken(UserRole.Patient);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/patients", ValidPatientBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Claims a ClinicAdmin would legitimately carry - so if any of the invalid
    // tokens below were accepted, the request would succeed (201), not 401.
    private static Claim[] ClinicAdminClaims() =>
    [
        new(JwtClaimNames.UserId, Guid.NewGuid().ToString()),
        new(JwtClaimNames.Role, nameof(UserRole.ClinicAdmin)),
        new(JwtClaimNames.ClinicId, Guid.NewGuid().ToString()),
    ];

    private async Task<HttpStatusCode> PostWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/patients", ValidPatientBody());

        return response.StatusCode;
    }

    [Fact]
    public async Task Post_WithTokenSignedByDifferentKey_Returns401()
    {
        var token = TestJwt.CreateToken(
            ClinicAdminClaims(),
            signingKey: "attacker-controlled-signing-key-at-least-32-bytes!!");

        Assert.Equal(HttpStatusCode.Unauthorized, await PostWithToken(token));
    }

    [Fact]
    public async Task Post_WithWrongIssuer_Returns401()
    {
        var token = TestJwt.CreateToken(ClinicAdminClaims(), issuer: "Some.Other.Issuer");

        Assert.Equal(HttpStatusCode.Unauthorized, await PostWithToken(token));
    }

    [Fact]
    public async Task Post_WithWrongAudience_Returns401()
    {
        var token = TestJwt.CreateToken(ClinicAdminClaims(), audience: "Some.Other.Audience");

        Assert.Equal(HttpStatusCode.Unauthorized, await PostWithToken(token));
    }

    [Fact]
    public async Task Post_WithUnsignedAlgNoneToken_Returns401()
    {
        var token = TestJwt.CreateUnsignedToken(UserRole.ClinicAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, await PostWithToken(token));
    }

    [Fact]
    public async Task Post_WithPatientTokenTamperedToClinicAdmin_Returns401()
    {
        // A real Patient token (would be 403 here) with its role claim rewritten
        // to ClinicAdmin. The signature no longer matches, so the escalation
        // must be rejected at authentication, not authorized.
        var patientToken = TestJwt.CreateValidToken(UserRole.Patient, Guid.NewGuid());
        var tampered = TestJwt.TamperPayload(
            patientToken,
            payload => payload.Replace(
                $"\"{JwtClaimNames.Role}\":\"{nameof(UserRole.Patient)}\"",
                $"\"{JwtClaimNames.Role}\":\"{nameof(UserRole.ClinicAdmin)}\""));

        Assert.NotEqual(patientToken, tampered);
        Assert.Equal(HttpStatusCode.Unauthorized, await PostWithToken(tampered));
    }
}
