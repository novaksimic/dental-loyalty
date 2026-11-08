using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Application.Loyalty;

namespace DentalLoyalty.Application.Loyalty.Commands;

// Records that a patient of the caller's own clinic used a reward they unlocked
// (BUSINESS.md §13, §19). The redemption deducts no points and no referral
// credits; it is saved with its RewardRedeemed transaction in one database
// transaction.
//
// Every lookup is scoped to the authenticated tenant (ICurrentTenant): a patient
// or reward of another clinic is indistinguishable from none. The Clinic Admin
// responsible is the authenticated caller (ICurrentUser).
//
// The cycle lifecycle is applied through LoyaltyCycleProgression (ADR-008): an
// expired cycle is ended and the next one started first, so the reward is
// evaluated in the cycle running now. When this redemption satisfies the
// cycle's last completion condition, the cycle is completed in the same
// database transaction as the redemption (BUSINESS.md §5.2, §13.1, §19 step 5).
//
// Throws ConcurrencyConflictException when the account changed concurrently and
// DuplicateRewardRedemptionException when the reward was redeemed concurrently in
// the same cycle; nothing is saved in either case.
public sealed class RedeemRewardHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly IRewardRepository _rewardRepository;
    private readonly ILoyaltyAccountRepository _loyaltyAccountRepository;
    private readonly LoyaltyCycleProgression _loyaltyCycleProgression;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public RedeemRewardHandler(
        IPatientRepository patientRepository,
        IRewardRepository rewardRepository,
        ILoyaltyAccountRepository loyaltyAccountRepository,
        LoyaltyCycleProgression loyaltyCycleProgression,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _patientRepository = patientRepository;
        _rewardRepository = rewardRepository;
        _loyaltyAccountRepository = loyaltyAccountRepository;
        _loyaltyCycleProgression = loyaltyCycleProgression;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<RedeemRewardResult> HandleAsync(
        RedeemRewardCommand command,
        CancellationToken cancellationToken = default)
    {
        // Fails closed like the other tenant-scoped patient use cases.
        if (!_currentTenant.HasTenant || !_currentUser.HasUser)
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.PatientNotFound);
        }

        var clinicId = _currentTenant.ClinicId!.Value;
        var redeemedBy = _currentUser.UserId!.Value;

        var patient = await _patientRepository.GetByIdForTenantAsync(
            command.PatientId,
            clinicId,
            cancellationToken);

        if (patient is null)
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.PatientNotFound);
        }

        var reward = await _rewardRepository.GetByIdForTenantAsync(
            command.RewardId,
            clinicId,
            cancellationToken);

        if (reward is null)
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.RewardNotFound);
        }

        if (!patient.IsActive)
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.PatientInactive);
        }

        if (!reward.IsActive)
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.RewardUnavailable);
        }

        // ADR-006: every Patient has its account from the moment it is created.
        var account = await _loyaltyAccountRepository.GetByPatientIdForTenantAsync(
            patient.Id,
            clinicId,
            cancellationToken)
            ?? throw new InvalidOperationException("The patient has no loyalty account.");

        var now = DateTime.UtcNow;

        var currentCycle = await _loyaltyCycleProgression.GetCurrentCycleAsync(
            account,
            now,
            save: true,
            cancellationToken);

        // No rewards before registration and linking (BUSINESS.md §5.1).
        if (currentCycle is null || patient.UserId is null)
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.NotEligible);
        }

        var redeemedRewardIds = await _rewardRepository.GetRedeemedRewardIdsAsync(
            currentCycle.Id,
            cancellationToken);

        // A repeated request is recognised before anything is written; the
        // database's unique key still stops one that races with it.
        if (redeemedRewardIds.Contains(reward.Id))
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.AlreadyRedeemed);
        }

        // Re-evaluated against the current balances at the time of the operation,
        // never taken from the client (BUSINESS.md §20, §23).
        if (!reward.IsUnlockedBy(account))
        {
            return RedeemRewardResult.Rejected(RedeemRewardStatus.NotEligible);
        }

        var (redemption, transaction) = account.RedeemReward(
            reward,
            currentCycle,
            alreadyRedeemedInCycle: false,
            redeemedBy,
            redeemedAt: now);

        var completion = await _loyaltyCycleProgression.CompleteIfSatisfiedAsync(
            account,
            currentCycle,
            redemption.RedeemedAt,
            redeemedNowRewardId: reward.Id,
            cancellationToken);

        await _loyaltyAccountRepository.SaveRewardRedemptionAsync(
            redemption,
            account,
            transaction,
            cancellationToken);

        return new RedeemRewardResult(
            RedeemRewardStatus.Redeemed,
            redemption.Id,
            redemption.RedeemedAt,
            CycleCompleted: completion is not null);
    }
}
