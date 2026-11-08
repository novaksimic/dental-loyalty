using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Loyalty;

namespace DentalLoyalty.Application.Loyalty;

// The patient's cycle as of now - null when they have none (not registered and
// linked, §5.1) - and the not-yet-saved transitions that led to it.
public sealed record LoyaltyCycleView(
    LoyaltyCycle? Current,
    IReadOnlyList<CycleTransition> PendingTransitions);

// Applies the loyalty cycle lifecycle (BUSINESS.md §5.2-§5.4;
// LoyaltyCycleLifecycle; ADR-008) for the use cases that read or change a
// patient's loyalty state, so every one of them handles expiry and completion
// the same way:
//
//   1. GetCurrentCycleAsync before the operation: the patient's cycle as of
//      now, ending any expired cycle first. Expiry happened at the cycle's
//      one-year end whatever triggers its processing, so it is saved on its own
//      - atomically - before the operation, and stays recorded even if the
//      operation then fails or is rejected.
//   2. CompleteIfSatisfiedAsync after the operation has changed the account or
//      recorded a redemption: the completion it causes, if any, is tracked in
//      the same unit of work and saved with the operation itself.
public sealed class LoyaltyCycleProgression
{
    private readonly ILoyaltyCycleRepository _loyaltyCycleRepository;
    private readonly ILoyaltyAccountRepository _loyaltyAccountRepository;
    private readonly IRewardRepository _rewardRepository;

    public LoyaltyCycleProgression(
        ILoyaltyCycleRepository loyaltyCycleRepository,
        ILoyaltyAccountRepository loyaltyAccountRepository,
        IRewardRepository rewardRepository)
    {
        _loyaltyCycleRepository = loyaltyCycleRepository;
        _loyaltyAccountRepository = loyaltyAccountRepository;
        _rewardRepository = rewardRepository;
    }

    // The patient's active cycle as of now, or null when the patient has none
    // (not registered and linked, §5.1). Every cycle that expired by now is
    // ended, the account reset and the next cycle started (§5.3, §5.4); with
    // save, that is persisted before returning - otherwise (a read-only view)
    // it is only applied to the loaded state.
    //
    // account must be the patient's account loaded through
    // ILoyaltyAccountRepository in this unit of work.
    public async Task<LoyaltyCycle?> GetCurrentCycleAsync(
        LoyaltyAccount account,
        DateTime now,
        bool save,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var activeCycle = await _loyaltyCycleRepository.GetActiveForPatientAsync(
            account.PatientId,
            account.ClinicId,
            cancellationToken);

        if (activeCycle is null)
        {
            return null;
        }

        var transitions = LoyaltyCycleLifecycle.RollOverExpired(account, activeCycle, now);

        if (transitions.Count == 0)
        {
            return activeCycle;
        }

        foreach (var transition in transitions)
        {
            _loyaltyAccountRepository.TrackCycleTransition(transition);
        }

        if (save)
        {
            await _loyaltyAccountRepository.SaveBalanceChangesAsync([account], [], cancellationToken);
        }

        return transitions[^1].Next;
    }

    // For read-only views that also present history (ADR-008 decision 6): the
    // patient's cycle as of now and the transitions that the next operation will
    // record for every cycle that expired by now - oldest first, empty when none
    // expired. They are applied to the loaded account and cycle only; nothing is
    // tracked for saving and nothing is saved.
    public async Task<LoyaltyCycleView> ViewCurrentCycleAsync(
        LoyaltyAccount account,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var activeCycle = await _loyaltyCycleRepository.GetActiveForPatientAsync(
            account.PatientId,
            account.ClinicId,
            cancellationToken);

        if (activeCycle is null)
        {
            return new LoyaltyCycleView(null, []);
        }

        var transitions = LoyaltyCycleLifecycle.RollOverExpired(account, activeCycle, now);

        return new LoyaltyCycleView(
            transitions.Count == 0 ? activeCycle : transitions[^1].Next,
            transitions);
    }

    // Completes the cycle if the operation that has just changed account - or
    // redeemed redeemedNowRewardId - satisfied its last completion condition
    // (§5.2), tracking the transition for the operation's own save. Returns the
    // transition, or null when the cycle continues.
    public async Task<CycleTransition?> CompleteIfSatisfiedAsync(
        LoyaltyAccount account,
        LoyaltyCycle activeCycle,
        DateTime now,
        Guid? redeemedNowRewardId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(activeCycle);

        var rewards = await _rewardRepository.ListForTenantAsync(account.ClinicId, cancellationToken);
        var redeemed = new HashSet<Guid>(
            await _rewardRepository.GetRedeemedRewardIdsAsync(activeCycle.Id, cancellationToken));

        if (redeemedNowRewardId is Guid rewardId)
        {
            redeemed.Add(rewardId);
        }

        var transition = LoyaltyCycleLifecycle.CompleteIfSatisfied(account, activeCycle, rewards, redeemed, now);

        if (transition is not null)
        {
            _loyaltyAccountRepository.TrackCycleTransition(transition);
        }

        return transition;
    }
}
