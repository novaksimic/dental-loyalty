using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Catalog.Commands;

// Activates or deactivates a service of the caller's own clinic (BUSINESS.md
// §15 "active/inactive status"). A deactivated service stays in the catalog and
// in every patient's history, but can no longer be recorded for a patient
// (RecordPatientServiceHandler: ServiceInactive). Services are never deleted.
//
// Idempotent: a service already in the requested state is left unchanged.
// Returns false when there is no such service in the caller's clinic -
// including one of another clinic, which is indistinguishable from none.
public sealed class SetServiceActiveHandler
{
    private readonly IServiceRepository _serviceRepository;
    private readonly ICurrentTenant _currentTenant;

    public SetServiceActiveHandler(
        IServiceRepository serviceRepository,
        ICurrentTenant currentTenant)
    {
        _serviceRepository = serviceRepository;
        _currentTenant = currentTenant;
    }

    public async Task<bool> HandleAsync(
        SetServiceActiveCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return false;
        }

        var service = await _serviceRepository.GetByIdForTenantAsync(
            command.ServiceId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        if (service is null)
        {
            return false;
        }

        if (service.IsActive == command.IsActive)
        {
            return true;
        }

        if (command.IsActive)
        {
            service.Activate();
        }
        else
        {
            service.Deactivate();
        }

        await _serviceRepository.UpdateAsync(service, cancellationToken);

        return true;
    }
}
