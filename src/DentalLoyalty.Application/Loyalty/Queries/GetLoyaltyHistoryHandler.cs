using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Loyalty.Queries;

public sealed record GetLoyaltyHistoryQuery(LoyaltyHistorySubject Subject);

// The current loyalty state (BUSINESS.md §5, §17): balances against their
// maximums, the active cycle - null before registration and linking (§5.1) -
// and each reward's state in it.
public sealed record LoyaltyStatusResult(
    int Points,
    int MaximumPoints,
    int ReferralBalance,
    int MaximumReferralRewards,
    DateTime? CycleStartedAt,
    DateTime? CycleExpiresAt,
    IReadOnlyList<PatientRewardResult> Rewards);

// One loyalty cycle (BUSINESS.md §5.1-§5.4). Points/Referrals are the current
// balances for the active cycle and the final ones - from its CycleReset - for
// an ended cycle. ExpiresAt is set only for the active cycle.
public sealed record LoyaltyCycleHistoryResult(
    LoyaltyCycleStatus Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    DateTime? ExpiresAt,
    int? Points,
    int? Referrals,
    IReadOnlyList<RedeemedRewardResult> Redemptions);

// A reward the patient used, confirmed by the Clinic Admin (§13.1). It consumed
// no points and no referral credits (§13).
public sealed record RedeemedRewardResult(
    string? RewardName,
    DateTime RedeemedAt);

// A referral the patient made (§4) and what it gave them: Rewarded with the
// points credited, or nothing - RewardsForfeited when §4.2's registration
// requirement was not met.
public sealed record ReferralHistoryResult(
    ReferralStatus Status,
    DateTime ReferredAt,
    DateTime? ExaminedAt,
    bool RewardsForfeited,
    bool Rewarded,
    int? CreditedPoints);

public sealed record LoyaltyHistoryResult(
    LoyaltyStatusResult Status,
    IReadOnlyList<LoyaltyCycleHistoryResult> Cycles,
    IReadOnlyList<ReferralHistoryResult> Referrals);

// The patient's loyalty history apart from the transactions, which are paged
// (GetLoyaltyTransactionsHandler): current state, every cycle with the rewards
// redeemed in it, and the referrals the patient made. All lists are newest
// first.
//
// Shown as of now (ADR-008 decision 6): a cycle that has expired is shown as
// Expired and the next one as active, with reset balances, exactly as the next
// operation will record it. Reading never saves anything.
public sealed class GetLoyaltyHistoryHandler
{
    private readonly LoyaltyHistoryStateLoader _stateLoader;
    private readonly ILoyaltyHistoryRepository _historyRepository;
    private readonly IRewardRepository _rewardRepository;

    public GetLoyaltyHistoryHandler(
        LoyaltyHistoryStateLoader stateLoader,
        ILoyaltyHistoryRepository historyRepository,
        IRewardRepository rewardRepository)
    {
        _stateLoader = stateLoader;
        _historyRepository = historyRepository;
        _rewardRepository = rewardRepository;
    }

    // Null when the patient is not found in the caller's clinic.
    public async Task<LoyaltyHistoryResult?> HandleAsync(
        GetLoyaltyHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var state = await _stateLoader.LoadAsync(query.Subject, cancellationToken);

        if (state is null)
        {
            return null;
        }

        var (patientId, clinicId) = (state.PatientId, state.ClinicId);

        var rewards = await _rewardRepository.ListForTenantAsync(clinicId, cancellationToken);
        var redemptions = await _historyRepository.ListRedemptionsAsync(patientId, clinicId, cancellationToken);
        var cycles = await _historyRepository.ListCyclesAsync(patientId, clinicId, cancellationToken);
        var referrals = await _historyRepository.ListReferralsMadeAsync(patientId, clinicId, cancellationToken);

        // Redemptions stay linked to the cycle they were made in (ADR-008
        // decision 7); a cycle started by a pending rollover has none.
        IReadOnlySet<Guid> redeemedInCurrentCycle = state.CurrentCycle is null
            ? new HashSet<Guid>()
            : await _rewardRepository.GetRedeemedRewardIdsAsync(state.CurrentCycle.Id, cancellationToken);

        var status = new LoyaltyStatusResult(
            state.Account.Points,
            LoyaltyAccount.MaximumPointsBalance,
            state.Account.ReferralBalance,
            LoyaltyAccount.MaximumReferralRewards,
            LoyaltyHistoryStateLoader.AsUtc(state.CurrentCycle?.StartedAt),
            LoyaltyHistoryStateLoader.AsUtc(state.CurrentCycle?.ExpiresAt),
            PatientRewardStatuses.Evaluate(rewards, state.Account, state.CurrentCycle, redeemedInCurrentCycle, state.Now));

        return new LoyaltyHistoryResult(
            status,
            BuildCycles(state, cycles, redemptions),
            referrals.Select(r => new ReferralHistoryResult(
                r.Status,
                LoyaltyHistoryStateLoader.AsUtc(r.ReferredAt),
                LoyaltyHistoryStateLoader.AsUtc(r.ExaminedAt),
                r.RewardsForfeited,
                Rewarded: r.CreditedPoints is not null,
                r.CreditedPoints)).ToList());
    }

    private static List<LoyaltyCycleHistoryResult> BuildCycles(
        LoyaltyHistoryState state,
        IReadOnlyList<LoyaltyCycleRecord> recorded,
        IReadOnlyList<RewardRedemptionRecord> redemptions)
    {
        var redemptionsByCycle = redemptions
            .GroupBy(r => r.LoyaltyCycleId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RedeemedRewardResult>)g
                    .Select(r => new RedeemedRewardResult(r.RewardName, LoyaltyHistoryStateLoader.AsUtc(r.RedeemedAt)))
                    .ToList());

        IReadOnlyList<RedeemedRewardResult> RedemptionsOf(Guid cycleId) =>
            redemptionsByCycle.TryGetValue(cycleId, out var list) ? list : [];

        // The pending transitions replace the stored active cycle: it and every
        // cycle after it up to now are shown as ended, then the cycle now running.
        var pendingEnded = state.PendingTransitions.Select(t => t.Ended.Id).ToHashSet();

        var stored = recorded
            .Where(c => !pendingEnded.Contains(c.Id))
            .Select(c => c.Status == LoyaltyCycleStatus.Active
                ? ActiveCycle(state, c.StartedAt, RedemptionsOf(c.Id))
                : new LoyaltyCycleHistoryResult(
                    c.Status,
                    LoyaltyHistoryStateLoader.AsUtc(c.StartedAt),
                    LoyaltyHistoryStateLoader.AsUtc(c.EndedAt),
                    ExpiresAt: null,
                    -c.ResetPoints,
                    -c.ResetReferrals,
                    RedemptionsOf(c.Id)));

        // Oldest first in PendingTransitions; shown newest first.
        var pending = state.PendingTransitions
            .Reverse()
            .Select(t => new LoyaltyCycleHistoryResult(
                t.Ended.Status,
                LoyaltyHistoryStateLoader.AsUtc(t.Ended.StartedAt),
                LoyaltyHistoryStateLoader.AsUtc(t.Ended.EndedAt),
                ExpiresAt: null,
                -t.Reset.PointsChange,
                -t.Reset.ReferralChange,
                RedemptionsOf(t.Ended.Id)));

        var result = new List<LoyaltyCycleHistoryResult>();

        if (state.PendingTransitions.Count > 0)
        {
            var current = state.PendingTransitions[^1].Next;
            result.Add(ActiveCycle(state, current.StartedAt, []));
            result.AddRange(pending);
        }

        result.AddRange(stored);

        return result
            .OrderByDescending(c => c.StartedAt)
            .ToList();
    }

    // The cycle now running: its progress is the account's current balances,
    // and it expires one year after its start (§5.3).
    private static LoyaltyCycleHistoryResult ActiveCycle(
        LoyaltyHistoryState state,
        DateTime startedAt,
        IReadOnlyList<RedeemedRewardResult> redemptions)
    {
        var start = LoyaltyHistoryStateLoader.AsUtc(startedAt);

        return new LoyaltyCycleHistoryResult(
            LoyaltyCycleStatus.Active,
            start,
            EndedAt: null,
            ExpiresAt: start.AddYears(1),
            state.Account.Points,
            state.Account.ReferralBalance,
            redemptions);
    }
}
