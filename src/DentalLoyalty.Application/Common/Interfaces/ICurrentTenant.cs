namespace DentalLoyalty.Application.Common.Interfaces;

// Trusted tenant context for the current request, resolved by the API layer from
// the authenticated user's JWT claims (never from client-supplied route/query/body
// values - SECURITY.md §6.1/§6.3). ClinicId doubles as the tenant identifier for
// this MVP (every tenant-scoped entity already stores ClinicId directly; see
// Slice 5 implementation notes) - there is no separate TenantId concept.
//
// PlatformAdmin operates at platform level and has no clinic of its own, so
// HasTenant is false and ClinicId is null for PlatformAdmin callers.
public interface ICurrentTenant
{
    bool HasTenant { get; }

    Guid? ClinicId { get; }
}
