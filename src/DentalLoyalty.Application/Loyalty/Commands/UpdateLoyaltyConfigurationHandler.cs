using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Loyalty.Commands;

public sealed class UpdateLoyaltyConfigurationHandler
{
    private readonly ILoyaltyConfigurationRepository _loyaltyConfigurationRepository;
    private readonly ICurrentTenant _currentTenant;

    public UpdateLoyaltyConfigurationHandler(
        ILoyaltyConfigurationRepository loyaltyConfigurationRepository,
        ICurrentTenant currentTenant)
    {
        _loyaltyConfigurationRepository = loyaltyConfigurationRepository;
        _currentTenant = currentTenant;
    }

    // Changes the scoring configuration of the caller's own clinic only
    // (BUSINESS.md §3.1, §18; SECURITY.md §5.2). Returns false when there is no
    // tenant or no configuration. Invalid values are rejected by
    // LoyaltyConfiguration with ArgumentOutOfRangeException before anything is
    // saved. Points already recorded for past services are not recalculated.
    public async Task<bool> HandleAsync(
        UpdateLoyaltyConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return false;
        }

        var configuration = await _loyaltyConfigurationRepository.GetByClinicIdAsync(
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        if (configuration is null)
        {
            return false;
        }

        configuration.UpdateScoring(command.SpendAmountRsd, command.PointsAwarded);

        await _loyaltyConfigurationRepository.UpdateAsync(configuration, cancellationToken);

        return true;
    }
}
