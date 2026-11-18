using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Catalog.Queries;

// One service of the caller's own clinic. Returns null both when the service
// does not exist and when it belongs to another clinic - the caller must not be
// able to tell the two apart.
public sealed class GetServiceByIdHandler
{
    private readonly IServiceRepository _serviceRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetServiceByIdHandler(
        IServiceRepository serviceRepository,
        ICurrentTenant currentTenant)
    {
        _serviceRepository = serviceRepository;
        _currentTenant = currentTenant;
    }

    public async Task<Service?> HandleAsync(
        GetServiceByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        return await _serviceRepository.GetByIdForTenantAsync(
            query.ServiceId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);
    }
}
