using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Rewards.Commands;

// Changes a reward definition of the caller's own clinic (BUSINESS.md §18,
// §22: names, descriptions and point/referral requirements are configurable).
//
// Only the definition changes. Reward availability is always derived from the
// patient's balances when it is read or redeemed (Reward.StatusFor,
// IsUnlockedBy), so the new requirements simply apply from now on;
// redemptions already recorded stay as they are.
//
// Tenant-scoped like the other id-based use cases: a reward of another clinic is
// indistinguishable from none, and a rejected update saves nothing.
public sealed class UpdateRewardHandler
{
    private readonly IRewardRepository _rewardRepository;
    private readonly ICurrentTenant _currentTenant;

    public UpdateRewardHandler(
        IRewardRepository rewardRepository,
        ICurrentTenant currentTenant)
    {
        _rewardRepository = rewardRepository;
        _currentTenant = currentTenant;
    }

    public async Task<RewardCommandResult> HandleAsync(
        UpdateRewardCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return RewardCommandResult.Rejected(RewardCommandStatus.NotFound);
        }

        var reward = await _rewardRepository.GetByIdForTenantAsync(
            command.RewardId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        if (reward is null)
        {
            return RewardCommandResult.Rejected(RewardCommandStatus.NotFound);
        }

        try
        {
            // Validates before changing anything, so an invalid update leaves
            // the loaded reward as it was.
            reward.Update(
                RewardInput.Name(command.Name),
                command.RequiredPoints,
                command.RequiredReferrals,
                RewardInput.Description(command.Description));
        }
        catch (ArgumentException ex)
        {
            return RewardCommandResult.Invalid(ex);
        }

        await _rewardRepository.UpdateAsync(reward, cancellationToken);

        return new RewardCommandResult(RewardCommandStatus.Saved, RewardDefinitionDto.From(reward));
    }
}
