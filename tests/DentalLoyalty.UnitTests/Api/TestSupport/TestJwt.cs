using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DentalLoyalty.UnitTests.Api.TestSupport;

// Issues tokens using the exact same JwtOptions values DentalLoyaltyApiFactory
// configures the API host with, so tokens minted here validate against it.
internal static class TestJwt
{
    public const string SigningKey = "integration-test-signing-key-at-least-32-bytes-long!!";

    public const string Issuer = "DentalLoyalty.Tests";

    public const string Audience = "DentalLoyalty.Tests.Client";

    public static string CreateValidToken(
        UserRole role,
        Guid? clinicId = null,
        Guid? userId = null)
    {
        var options = new JwtOptions
        {
            SigningKey = SigningKey,
            Issuer = Issuer,
            Audience = Audience,
            AccessTokenLifetimeMinutes = 60,
        };

        var generator = new JwtTokenGenerator(Options.Create(options));

        return generator.GenerateToken(userId ?? Guid.NewGuid(), role, clinicId);
    }

    // Builds a token with exactly the given claims, bypassing JwtTokenGenerator's
    // guarantees (e.g. always emitting userId) so tests can present the API with
    // tokens the real issuer would never produce: missing/malformed claims, or a
    // different signing key/issuer/audience.
    public static string CreateToken(
        IEnumerable<Claim> claims,
        string signingKey = SigningKey,
        string issuer = Issuer,
        string audience = Audience)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(60),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // A well-formed token with "alg":"none" and no signature.
    public static string CreateUnsignedToken(UserRole role, Guid? clinicId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtClaimNames.UserId, Guid.NewGuid().ToString()),
            new(JwtClaimNames.Role, role.ToString()),
        };

        if (clinicId is not null)
        {
            claims.Add(new Claim(JwtClaimNames.ClinicId, clinicId.Value.ToString()));
        }

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, claims, now, now.AddMinutes(60));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Takes a genuinely signed token and rewrites its payload (e.g. to escalate
    // the role claim) while keeping the original signature - what an attacker
    // holding a real low-privilege token can do without the signing key.
    public static string TamperPayload(string token, Func<string, string> rewritePayloadJson)
    {
        var parts = token.Split('.');
        var payloadJson = Base64UrlEncoder.Decode(parts[1]);

        parts[1] = Base64UrlEncoder.Encode(rewritePayloadJson(payloadJson));

        return string.Join('.', parts);
    }

    // Built independently of JwtTokenGenerator (whose lifetime is always relative
    // to "now") so both NotBefore and Expires can be placed safely in the past.
    public static string CreateExpiredToken(UserRole role)
    {
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var issuedAt = DateTime.UtcNow.AddHours(-2);

        var claims = new[]
        {
            new Claim(JwtClaimNames.UserId, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Role, role.ToString()),
        };

        var expiredToken = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: issuedAt,
            expires: issuedAt.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(expiredToken);
    }
}
