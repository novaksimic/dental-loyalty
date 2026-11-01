using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Loyalty.Queries;

public sealed class GetLoyaltyConfigurationHandler
{
    private readonly ILoyaltyConfigurationRepository _loyaltyConfigurationRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetLoyaltyConfigurationHandler(
        ILoyaltyConfigurationRepository loyaltyConfigurationRepository,
        ICurrentTenant currentTenant)
    {
        _loyaltyConfigurationRepository = loyaltyConfigurationRepository;
        _currentTenant = currentTenant;
    }

    // Returns the configuration of the caller's own clinic only; a caller with
    // no tenant gets null.
    public async Task<LoyaltyConfiguration?> HandleAsync(
        GetLoyaltyConfigurationQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        return await _loyaltyConfigurationRepository.GetByClinicIdAsync(
            _currentTenant.ClinicId!.Value,
            cancellationToken);
    }
}
