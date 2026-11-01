using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace DentalLoyalty.Infrastructure.Security;

// Uses Microsoft.Extensions.Identity.Core's PasswordHasher<TUser> in isolation,
// purely as a proven hashing algorithm - not as part of the ASP.NET Core Identity
// membership system (no UserManager/SignInManager/IdentityDbContext/IdentityUser
// are introduced). The generic "user" argument is unused by the default
// implementation, so it is passed as null.
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password)
    {
        return _hasher.HashPassword(null!, password);
    }

    public bool Verify(string password, string passwordHash)
    {
        var result = _hasher.VerifyHashedPassword(null!, passwordHash, password);

        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
