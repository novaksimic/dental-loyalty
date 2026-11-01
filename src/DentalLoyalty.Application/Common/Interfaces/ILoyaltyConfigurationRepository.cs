using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

public interface ILoyaltyConfigurationRepository
{
    // The lookup is keyed by clinicId alone, so callers must pass the trusted
    // tenant (ICurrentTenant), never a client-supplied value.
    Task<LoyaltyConfiguration?> GetByClinicIdAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        LoyaltyConfiguration configuration,
        CancellationToken cancellationToken = default);
}
