using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DentalLoyalty.Infrastructure.Security;

public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken(
        Guid userId,
        UserRole role,
        Guid? clinicId)
    {
        var claims = new List<Claim>
        {
            new(JwtClaimNames.UserId, userId.ToString()),
            new(JwtClaimNames.Role, role.ToString()),
        };

        if (clinicId is not null)
        {
            claims.Add(new Claim(JwtClaimNames.ClinicId, clinicId.Value.ToString()));
        }

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.SigningKey));

        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.AccessTokenLifetimeMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
