using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Rewards.Commands;

// Activates or deactivates a reward of the caller's own clinic (BUSINESS.md §22
// "reward availability"). Rewards are never deleted: redemptions refer to them.
//
// Only the definition changes; the existing rules then apply unchanged:
//   - an inactive reward is never Available and cannot be redeemed
//     (Reward.StatusFor; RedeemRewardHandler: RewardUnavailable);
//   - a redemption already recorded in the patient's current cycle is still
//     shown as Redeemed (PatientRewardStatuses);
//   - a withdrawn reward does not block cycle completion (ADR-008 decision 2),
//     which is evaluated by the patient's next loyalty operation, not here.
//
// Idempotent: a reward already in the requested state is left unchanged.
// Returns false when there is no such reward in the caller's clinic - including
// one of another clinic, which is indistinguishable from none.
public sealed class SetRewardActiveHandler
{
    private readonly IRewardRepository _rewardRepository;
    private readonly ICurrentTenant _currentTenant;

    public SetRewardActiveHandler(
        IRewardRepository rewardRepository,
        ICurrentTenant currentTenant)
    {
        _rewardRepository = rewardRepository;
        _currentTenant = currentTenant;
    }

    public async Task<bool> HandleAsync(
        SetRewardActiveCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return false;
        }

        var reward = await _rewardRepository.GetByIdForTenantAsync(
            command.RewardId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        if (reward is null)
        {
            return false;
        }

        if (reward.IsActive == command.IsActive)
        {
            return true;
        }

        if (command.IsActive)
        {
            reward.Activate();
        }
        else
        {
            reward.Deactivate();
        }

        await _rewardRepository.UpdateAsync(reward, cancellationToken);

        return true;
    }
}
