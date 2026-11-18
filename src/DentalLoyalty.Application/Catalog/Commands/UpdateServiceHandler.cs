using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Catalog.Commands;

// Changes the name and price of a service of the caller's own clinic
// (BUSINESS.md §18 "manage services and prices"). Services already recorded
// for patients keep the price they had (PatientService.PriceAtTimeOfService).
//
// Tenant-scoped like the other id-based use cases: a service of another clinic
// is indistinguishable from none, and a rejected update saves nothing.
public sealed class UpdateServiceHandler
{
    private readonly IServiceRepository _serviceRepository;
    private readonly ICurrentTenant _currentTenant;

    public UpdateServiceHandler(
        IServiceRepository serviceRepository,
        ICurrentTenant currentTenant)
    {
        _serviceRepository = serviceRepository;
        _currentTenant = currentTenant;
    }

    public async Task<ServiceCommandResult> HandleAsync(
        UpdateServiceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return ServiceCommandResult.Rejected(ServiceCommandStatus.NotFound);
        }

        var service = await _serviceRepository.GetByIdForTenantAsync(
            command.ServiceId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        if (service is null)
        {
            return ServiceCommandResult.Rejected(ServiceCommandStatus.NotFound);
        }

        try
        {
            // Validates before changing anything, so an invalid update leaves
            // the loaded service as it was.
            service.Update(command.Name?.Trim()!, command.Price);
        }
        catch (ArgumentException ex)
        {
            return ServiceCommandResult.Invalid(ex);
        }

        await _serviceRepository.UpdateAsync(service, cancellationToken);

        return new ServiceCommandResult(ServiceCommandStatus.Saved, service);
    }
}
