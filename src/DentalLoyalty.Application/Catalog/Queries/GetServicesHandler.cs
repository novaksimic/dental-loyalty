using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Catalog.Queries;

// The caller's own clinic's service catalog, ordered by name (BUSINESS.md §15,
// §18). Only the authenticated tenant's services are ever read. Returns null
// when the caller has no trusted tenant.
public sealed class GetServicesHandler
{
    private readonly IServiceRepository _serviceRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetServicesHandler(
        IServiceRepository serviceRepository,
        ICurrentTenant currentTenant)
    {
        _serviceRepository = serviceRepository;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<Service>?> HandleAsync(
        GetServicesQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        return await _serviceRepository.ListForTenantAsync(
            _currentTenant.ClinicId!.Value,
            query.IsActive,
            cancellationToken);
    }
}
