using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Domain.Loyalty;

// One cycle ending and the next starting (BUSINESS.md §5.2-§5.4): the ended
// cycle, the next active cycle starting at its end, and the CycleReset
// transaction recording the reset of the account's active progress. Persisted
// together, in one database transaction, or not at all.
public sealed record CycleTransition(
    LoyaltyCycle Ended,
    LoyaltyCycle Next,
    LoyaltyTransaction Reset);

// The only rules that end a loyalty cycle and start the next one (BUSINESS.md
// §5.2-§5.4; ADR-008). Every operation that can affect a cycle goes through
// these, so completion and expiry are evaluated the same way everywhere.
public static class LoyaltyCycleLifecycle
{
    // BUSINESS.md §5.3: a cycle that was not completed expires one year after
    // its start, and the next one starts at that moment (§5.4). Ends every
    // expired cycle up to the given time - one after another, when the patient
    // was inactive for more than a year - and returns the transitions, oldest
    // first, with the account reset for the cycle now running. Empty when
    // activeCycle has not expired.
    public static IReadOnlyList<CycleTransition> RollOverExpired(
        LoyaltyAccount account,
        LoyaltyCycle activeCycle,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(activeCycle);

        var transitions = new List<CycleTransition>();
        var current = activeCycle;

        while (current.HasExpiredAt(now))
        {
            current.End(LoyaltyCycleStatus.Expired, current.ExpiresAt);
            var transition = StartNext(account, current);
            transitions.Add(transition);
            current = transition.Next;
        }

        return transitions;
    }

    // BUSINESS.md §5.2: completes the active cycle when the operation that has
    // just changed the account - or recorded a redemption - satisfied its last
    // completion condition (LoyaltyCycle.IsCompletedBy), and starts the next
    // one at that moment. Returns null when the cycle continues.
    // redeemedRewardIds must include a redemption made by that operation.
    public static CycleTransition? CompleteIfSatisfied(
        LoyaltyAccount account,
        LoyaltyCycle activeCycle,
        IEnumerable<Reward> clinicRewards,
        IReadOnlySet<Guid> redeemedRewardIds,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(activeCycle);

        if (!activeCycle.IsCompletedBy(account, clinicRewards, redeemedRewardIds, now))
        {
            return null;
        }

        activeCycle.End(LoyaltyCycleStatus.Completed, now);
        return StartNext(account, activeCycle);
    }

    private static CycleTransition StartNext(LoyaltyAccount account, LoyaltyCycle ended)
    {
        var reset = account.ResetForEndedCycle(ended);
        return new CycleTransition(ended, LoyaltyCycle.StartNext(ended), reset);
    }
}
