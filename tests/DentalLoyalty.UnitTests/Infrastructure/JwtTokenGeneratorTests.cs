using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

public class JwtTokenGeneratorTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "unit-test-signing-key-at-least-32-bytes-long!!",
        Issuer = "DentalLoyalty.Tests",
        Audience = "DentalLoyalty.Tests.Client",
        AccessTokenLifetimeMinutes = 60,
    };

    private static JwtTokenGenerator CreateGenerator(JwtOptions options)
    {
        return new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(options));
    }

    private static TokenValidationParameters CreateValidationParameters(JwtOptions options)
    {
        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            ValidateLifetime = true,
        };
    }

    [Fact]
    public void GenerateToken_ForPlatformAdmin_ContainsUserIdAndRole_AndNoClinicId()
    {
        var generator = CreateGenerator(Options);
        var userId = Guid.NewGuid();

        var token = generator.GenerateToken(userId, UserRole.PlatformAdmin, null);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == JwtClaimNames.UserId).Value);
        Assert.Equal(nameof(UserRole.PlatformAdmin), jwt.Claims.Single(c => c.Type == JwtClaimNames.Role).Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == JwtClaimNames.ClinicId);
    }

    [Fact]
    public void GenerateToken_ForClinicAdmin_ContainsUserIdRoleAndClinicId()
    {
        var generator = CreateGenerator(Options);
        var userId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();

        var token = generator.GenerateToken(userId, UserRole.ClinicAdmin, clinicId);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == JwtClaimNames.UserId).Value);
        Assert.Equal(nameof(UserRole.ClinicAdmin), jwt.Claims.Single(c => c.Type == JwtClaimNames.Role).Value);
        Assert.Equal(clinicId.ToString(), jwt.Claims.Single(c => c.Type == JwtClaimNames.ClinicId).Value);
    }

    [Fact]
    public void GenerateToken_ForPatient_ContainsUserIdRoleAndClinicId()
    {
        var generator = CreateGenerator(Options);
        var userId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();

        var token = generator.GenerateToken(userId, UserRole.Patient, clinicId);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(userId.ToString(), jwt.Claims.Single(c => c.Type == JwtClaimNames.UserId).Value);
        Assert.Equal(nameof(UserRole.Patient), jwt.Claims.Single(c => c.Type == JwtClaimNames.Role).Value);
        Assert.Equal(clinicId.ToString(), jwt.Claims.Single(c => c.Type == JwtClaimNames.ClinicId).Value);
    }

    [Fact]
    public void GenerateToken_CanBeValidated_WithConfiguredIssuerAudienceAndSigningKey()
    {
        var generator = CreateGenerator(Options);
        var token = generator.GenerateToken(Guid.NewGuid(), UserRole.ClinicAdmin, Guid.NewGuid());

        var principal = new JwtSecurityTokenHandler().ValidateToken(
            token,
            CreateValidationParameters(Options),
            out _);

        Assert.NotNull(principal);
    }

    [Fact]
    public void ExpiredToken_FailsValidation_WithConfiguredValidationParameters()
    {
        // Built independently of JwtTokenGenerator (whose lifetime is always
        // relative to "now") so the token's NotBefore/Expires can both be
        // placed safely in the past - this exercises the validation
        // configuration itself, not the generator.
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var issuedAt = DateTime.UtcNow.AddHours(-2);

        var expiredToken = new JwtSecurityToken(
            issuer: Options.Issuer,
            audience: Options.Audience,
            claims: [],
            notBefore: issuedAt,
            expires: issuedAt.AddMinutes(5),
            signingCredentials: credentials);

        var token = new JwtSecurityTokenHandler().WriteToken(expiredToken);

        var validationParameters = CreateValidationParameters(Options);
        validationParameters.ClockSkew = TimeSpan.Zero;

        Assert.Throws<SecurityTokenExpiredException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _));
    }
}
