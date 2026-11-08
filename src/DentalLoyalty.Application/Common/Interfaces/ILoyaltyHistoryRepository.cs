using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Common.Interfaces;

// Read-only access to one patient's recorded loyalty history (BUSINESS.md §5.4,
// §14, §18): loyalty transactions, cycles, reward redemptions and the referrals
// the patient made. Every query is scoped to patientId AND clinicId - clinicId
// must come from the trusted tenant context (ICurrentTenant), never from the
// request - and nothing is tracked or written.
//
// Ordering is newest first and deterministic, with the record's id as the last
// tie-breaker.
public interface ILoyaltyHistoryRepository
{
    Task<int> CountTransactionsAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // Newest first. At the same time, a CycleReset sorts as the later record: a
    // completion is recorded at the time of the operation that caused it.
    Task<IReadOnlyList<LoyaltyTransactionRecord>> ListTransactionsAsync(
        Guid patientId,
        Guid clinicId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    // Every cycle of the patient, newest first.
    Task<IReadOnlyList<LoyaltyCycleRecord>> ListCyclesAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // Every reward redemption of the patient, newest first.
    Task<IReadOnlyList<RewardRedemptionRecord>> ListRedemptionsAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // The referrals in which the patient is the referrer, newest first.
    Task<IReadOnlyList<ReferralRecord>> ListReferralsMadeAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default);
}

// One loyalty transaction with what it refers to, by name rather than id:
// the catalog service (ServicePointsEarned), the reward (RewardRedeemed),
// whether the patient was the referrer or the referred patient
// (ReferralPointsEarned), or how the cycle ended (CycleReset).
public sealed record LoyaltyTransactionRecord(
    DateTime OccurredAt,
    LoyaltyTransactionType Type,
    int PointsChange,
    int ReferralChange,
    string? ServiceName,
    string? RewardName,
    bool? IsReferrer,
    LoyaltyCycleStatus? EndedCycleStatus);

// One loyalty cycle as recorded. ResetPoints/ResetReferrals are the changes of
// its CycleReset transaction (the negated final balances); null while active.
public sealed record LoyaltyCycleRecord(
    Guid Id,
    LoyaltyCycleStatus Status,
    DateTime StartedAt,
    DateTime? EndedAt,
    int? ResetPoints,
    int? ResetReferrals);

public sealed record RewardRedemptionRecord(
    Guid LoyaltyCycleId,
    string? RewardName,
    DateTime RedeemedAt);

// One referral the patient made. CreditedPoints is the points of this patient's
// ReferralPointsEarned transaction for it; null when the patient received no
// referral reward for it.
public sealed record ReferralRecord(
    ReferralStatus Status,
    DateTime ReferredAt,
    DateTime? ExaminedAt,
    bool RewardsForfeited,
    int? CreditedPoints);
