using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

// The clinic's service catalog (BUSINESS.md §15).
public interface IServiceRepository
{
    // Returns the catalog service only if it belongs to clinicId - a service in
    // another clinic is indistinguishable from none. clinicId must come from the
    // trusted tenant context (ICurrentTenant), never from the request.
    Task<Service?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default);
}
