using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Clinics.Commands;

public sealed class CreateClinicHandler
{
    private readonly IClinicRepository _clinicRepository;

    public CreateClinicHandler(
        IClinicRepository clinicRepository)
    {
        _clinicRepository = clinicRepository;
    }

    public async Task<Guid> HandleAsync(
        CreateClinicCommand command,
        CancellationToken cancellationToken = default)
    {
        var clinic = new Clinic(command.Name);

        // BUSINESS.md §3.1 / SECURITY.md §5.1: the platform provisions the default
        // scoring configuration for every new clinic; the ClinicAdmin can change it.
        var loyaltyConfiguration = LoyaltyConfiguration.CreateDefault(clinic.Id);

        await _clinicRepository.AddAsync(
            clinic,
            loyaltyConfiguration,
            cancellationToken);

        return clinic.Id;
    }
}