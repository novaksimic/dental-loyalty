using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

// The clinic's service catalog (BUSINESS.md §15). clinicId must come from the
// trusted tenant context (ICurrentTenant), never from the request.
public interface IServiceRepository
{
    // Returns the catalog service only if it belongs to clinicId - a service in
    // another clinic is indistinguishable from none.
    Task<Service?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // The clinic's services ordered by name; only those with the given status
    // when isActive is set.
    Task<IReadOnlyList<Service>> ListForTenantAsync(
        Guid clinicId,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Service service,
        CancellationToken cancellationToken = default);

    // Saves changes to a service loaded through GetByIdForTenantAsync.
    Task UpdateAsync(
        Service service,
        CancellationToken cancellationToken = default);
}
