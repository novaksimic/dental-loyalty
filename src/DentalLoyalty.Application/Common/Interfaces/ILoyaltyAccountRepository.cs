using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Loyalty;

namespace DentalLoyalty.Application.Common.Interfaces;

// Persistence for balance-changing operations (ADR-004, ADR-005).
public interface ILoyaltyAccountRepository
{
    // Returns the patient's account only if it belongs to clinicId - an account in
    // another clinic is indistinguishable from none. clinicId must come from the
    // trusted tenant context (ICurrentTenant), never from the request.
    Task<LoyaltyAccount?> GetByPatientIdForTenantAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // Persists one balance-changing business operation in a single database
    // transaction: the changes to the given accounts (loaded through this
    // repository), the new transaction records, any cycle transitions tracked
    // with TrackCycleTransition, and any other change the operation made in the
    // same unit of work. Either everything is saved or nothing is. Every
    // transaction must belong to one of the given accounts.
    //
    // Throws ConcurrencyConflictException if any of the accounts was changed by
    // another operation after it was loaded, or another operation already ended
    // one of the tracked cycles; nothing is persisted in that case.
    Task SaveBalanceChangesAsync(
        IReadOnlyCollection<LoyaltyAccount> accounts,
        IReadOnlyCollection<LoyaltyTransaction> transactions,
        CancellationToken cancellationToken = default);

    // Adds a cycle transition (LoyaltyCycleLifecycle) - the ended cycle, loaded
    // through ILoyaltyCycleRepository, the next cycle and the CycleReset
    // transaction - to this unit of work. It is never saved on its own: the next
    // save of this repository persists it atomically with the operation that
    // caused it, and the reset account must be one of that save's accounts.
    void TrackCycleTransition(CycleTransition transition);

    // Records a service occurrence that earns points together with the account
    // change and the ServicePointsEarned transaction for it, in the same single
    // database transaction as SaveBalanceChangesAsync: all of it or nothing. The
    // transaction must be the one the account returned for this occurrence.
    //
    // Throws ConcurrencyConflictException as SaveBalanceChangesAsync does, and
    // DuplicateServiceOccurrenceException if the occurrence was already recorded;
    // nothing is persisted in either case.
    Task SaveServicePointsAsync(
        PatientService patientService,
        LoyaltyAccount account,
        LoyaltyTransaction transaction,
        CancellationToken cancellationToken = default);

    // Records a reward redemption together with its RewardRedeemed transaction in
    // one database transaction: both or neither (BUSINESS.md §13.1, §19). The
    // redemption leaves the account's balances unchanged, but the account's
    // version is still checked, because its eligibility was decided from them.
    //
    // Throws ConcurrencyConflictException if the account was changed by another
    // operation after it was loaded, and DuplicateRewardRedemptionException if
    // the reward was already redeemed in that cycle; nothing is persisted in
    // either case.
    Task SaveRewardRedemptionAsync(
        RewardRedemption redemption,
        LoyaltyAccount account,
        LoyaltyTransaction transaction,
        CancellationToken cancellationToken = default);

    // Records a validated referral together with the changes to both patients'
    // accounts and the ReferralPointsEarned transactions it generated (none, one
    // or two), in one database transaction: all of it or nothing (BUSINESS.md
    // §4.2). Both accounts' versions are checked even when an account was not
    // credited, because whether it was depended on its balances; the referral's
    // own version is checked too.
    //
    // Throws ConcurrencyConflictException if the referral or either account was
    // changed by another operation after it was loaded, and
    // ReferralAlreadyValidatedException if the referral's rewards were already
    // recorded; nothing is persisted in either case.
    Task SaveReferralValidationAsync(
        Referral referral,
        LoyaltyAccount referrerAccount,
        LoyaltyAccount referredAccount,
        IReadOnlyCollection<LoyaltyTransaction> transactions,
        CancellationToken cancellationToken = default);
}
