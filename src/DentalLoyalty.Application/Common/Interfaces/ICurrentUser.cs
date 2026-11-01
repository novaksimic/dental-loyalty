namespace DentalLoyalty.Application.Common.Interfaces;

// Trusted identity of the authenticated caller, resolved by the API layer from
// the already-validated JWT "userId" claim - the user-level counterpart of
// ICurrentTenant (same source, same rules). Never populated from client-supplied
// route/query/body values (SECURITY.md §6.1, BACKEND & API ARCHITECTURE.md §5:
// "The backend must never trust user identity information supplied directly by
// the client").
//
// Used for resource ownership ("own record only") checks, always in combination
// with ICurrentTenant - ownership never replaces the tenant boundary.
public interface ICurrentUser
{
    bool HasUser { get; }

    Guid? UserId { get; }
}
