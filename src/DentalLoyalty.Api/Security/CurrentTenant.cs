using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Infrastructure.Security;

namespace DentalLoyalty.Api.Security;

// The one place allowed to read tenant identity off HttpContext/claims (Security
// Pillar §6.2: JWT -> User -> TenantId -> Tenant Context). Everything else
// (Application handlers, repositories) consumes ICurrentTenant instead of
// touching HttpContext, ClaimsPrincipal, or JWT details directly.
//
// Resolved once per request from the already-validated JWT (signature/issuer/
// audience/lifetime checked by JWT Bearer authentication - Slice 4) "clinicId"
// claim. Treats a missing claim, an unauthenticated request, or a malformed
// claim value identically: no tenant. Never throws.
public sealed class CurrentTenant : ICurrentTenant
{
    public CurrentTenant(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var clinicIdClaim = user.FindFirst(JwtClaimNames.ClinicId)?.Value;

        if (Guid.TryParse(clinicIdClaim, out var clinicId))
        {
            ClinicId = clinicId;
        }
    }

    public bool HasTenant => ClinicId is not null;

    public Guid? ClinicId { get; }
}
