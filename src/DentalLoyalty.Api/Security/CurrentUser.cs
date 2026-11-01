using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Infrastructure.Security;

namespace DentalLoyalty.Api.Security;

// Reads the authenticated user's id off the same validated JWT principal that
// CurrentTenant reads the tenant from (JWT Bearer authentication - Slice 4 -
// has already checked signature/issuer/audience/lifetime). Like CurrentTenant,
// treats a missing claim, an unauthenticated request, or a malformed claim
// value identically: no user. Never throws.
public sealed class CurrentUser : ICurrentUser
{
    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userIdClaim = user.FindFirst(JwtClaimNames.UserId)?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            UserId = userId;
        }
    }

    public bool HasUser => UserId is not null;

    public Guid? UserId { get; }
}
