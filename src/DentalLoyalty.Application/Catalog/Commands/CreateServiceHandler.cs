using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Catalog.Commands;

// Adds a service to the caller's own clinic's catalog (BUSINESS.md §15, §18,
// §21). The clinic is the authenticated tenant (ICurrentTenant), never a value
// from the request. A new service is active.
public sealed class CreateServiceHandler
{
    private readonly IServiceRepository _serviceRepository;
    private readonly ICurrentTenant _currentTenant;

    public CreateServiceHandler(
        IServiceRepository serviceRepository,
        ICurrentTenant currentTenant)
    {
        _serviceRepository = serviceRepository;
        _currentTenant = currentTenant;
    }

    public async Task<ServiceCommandResult> HandleAsync(
        CreateServiceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return ServiceCommandResult.Rejected(ServiceCommandStatus.NoTenant);
        }

        Service service;

        try
        {
            service = new Service(_currentTenant.ClinicId!.Value, command.Name?.Trim()!, command.Price);
        }
        catch (ArgumentException ex)
        {
            return ServiceCommandResult.Invalid(ex);
        }

        await _serviceRepository.AddAsync(service, cancellationToken);

        return new ServiceCommandResult(ServiceCommandStatus.Saved, service);
    }
}
