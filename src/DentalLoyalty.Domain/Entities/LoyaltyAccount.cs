using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;

namespace DentalLoyalty.Domain.Entities;

public class LoyaltyAccount
{
    // BUSINESS.md §5/§8/§22: the balance can never exceed 1,000,000 points.
    public const int MaximumPointsBalance = 1_000_000;

    // BUSINESS.md §4.2: each referral reward is 50,000 points, and a patient
    // receives at most 5 of them per loyalty cycle (ADR-007).
    public const int ReferralRewardPoints = 50_000;

    public const int MaximumReferralRewards = 5;

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid PatientId { get; private set; }

    // Always between 0 and MaximumPointsBalance. Changes only through operations
    // that also return the LoyaltyTransaction explaining them (ADR-004).
    public int Points { get; private set; }

    // Referral credits (ADR-004; BACKEND & API ARCHITECTURE.md §9, §15) that
    // reward eligibility is evaluated against (BUSINESS.md §10). Between 0 and
    // MaximumReferralRewards; only a referral reward increases it (§4.2), and a
    // reward redemption never reduces it (§13). It is the cycle's referral
    // progress (§5.4), so it is also the number of referral rewards received in
    // the active cycle (ADR-007).
    public int ReferralBalance { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    // Optimistic concurrency token (ADR-005): a SQL Server rowversion generated
    // by the database on every insert and update of the row. Never set by code,
    // never exposed to or accepted from clients.
    public byte[] RowVersion { get; private set; } = [];

    private LoyaltyAccount()
    {
    }

    public LoyaltyAccount(
        Guid clinicId,
        Guid patientId)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("ClinicId is required.", nameof(clinicId));
        }

        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("PatientId is required.", nameof(patientId));
        }

        Id = Guid.NewGuid();
        ClinicId = clinicId;
        PatientId = patientId;
        Points = 0;
        ReferralBalance = 0;
        CreatedAt = DateTime.UtcNow;
    }

    // ADR-006: every Patient gets its account in the operation that creates the
    // Patient. The account's tenant is always the Patient's own ClinicId, never a
    // separately supplied value (ADR-006 §3), and it starts at 0 points with no
    // transactions (§4). Opening the account starts no loyalty cycle - that
    // happens only on registration and account linking (BUSINESS.md §5.1).
    public static LoyaltyAccount OpenFor(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        return new LoyaltyAccount(patient.ClinicId, patient.Id);
    }

    // Credits the points a recorded service generated (BUSINESS.md §3.1) and
    // returns the ServicePointsEarned transaction for it, linked to that service
    // occurrence. Only a service the patient's active cycle accepts earns points
    // (LoyaltyCycle.AcceptsService; BUSINESS.md §5.1). Points that would take the
    // balance above MaximumPointsBalance are not credited; the transaction
    // records only the points actually credited, which may be 0 when the
    // balance is already at the maximum.
    public LoyaltyTransaction EarnServicePoints(
        PatientService patientService,
        LoyaltyCycle activeCycle)
    {
        ArgumentNullException.ThrowIfNull(patientService);
        ArgumentNullException.ThrowIfNull(activeCycle);

        if (patientService.PatientId != PatientId
            || patientService.ClinicId != ClinicId)
        {
            throw new DomainException(
                "The service was not recorded for this loyalty account's patient.");
        }

        if (!activeCycle.AcceptsService(patientService))
        {
            throw new DomainException(
                "The service does not earn points in the patient's active loyalty cycle.");
        }

        if (patientService.RecordedBy is not Guid recordedBy)
        {
            throw new DomainException(
                "The user who recorded the service is unknown.");
        }

        var credited = CreditPoints(patientService.EarnedPoints);

        return LoyaltyTransaction.ServicePointsEarned(
            this,
            patientService,
            credited,
            recordedBy);
    }

    // Records that the patient used a reward they unlocked in their active cycle
    // (BUSINESS.md §13.1, §19) and returns the redemption with its RewardRedeemed
    // transaction. Neither balance changes: a redemption consumes no points and
    // no referral credits (§13), so other unlocked rewards stay unlocked (§12).
    // A reward is redeemed at most once per cycle (§5.2, §9);
    // alreadyRedeemedInCycle is whether it already was.
    public (RewardRedemption Redemption, LoyaltyTransaction Transaction) RedeemReward(
        Reward reward,
        LoyaltyCycle activeCycle,
        bool alreadyRedeemedInCycle,
        Guid redeemedBy)
    {
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(activeCycle);

        if (redeemedBy == Guid.Empty)
        {
            throw new ArgumentException("The user confirming the redemption is required.", nameof(redeemedBy));
        }

        // BUSINESS.md §21: a clinic's rewards are only for its own patients.
        if (reward.ClinicId != ClinicId)
        {
            throw new DomainException(
                "The reward does not belong to this loyalty account's clinic.");
        }

        if (activeCycle.PatientId != PatientId || activeCycle.ClinicId != ClinicId)
        {
            throw new DomainException(
                "The loyalty cycle does not belong to this loyalty account's patient.");
        }

        var now = DateTime.UtcNow;

        if (reward.StatusFor(this, activeCycle, alreadyRedeemedInCycle, now) != RewardStatus.Available)
        {
            throw new DomainException(
                "The reward is not available to this patient.");
        }

        var redemption = new RewardRedemption(this, reward, activeCycle, redeemedBy, now);

        return (redemption, LoyaltyTransaction.RewardRedeemed(this, redemption));
    }

    // Credits this patient's referral reward for a referral that is being
    // validated (BUSINESS.md §4.2; ADR-007): 50,000 points, within the maximum
    // balance (§3.3), and one referral credit. Returns null - nothing is credited
    // - when the patient has already received the maximum number of referral
    // rewards in the active cycle; the other patient of the referral is
    // unaffected. Called only by Referral.Validate, once it has established that
    // the referral generates rewards.
    internal LoyaltyTransaction? EarnReferralReward(Referral referral, Guid validatedBy)
    {
        if (ReferralBalance >= MaximumReferralRewards)
        {
            return null;
        }

        var credited = CreditPoints(ReferralRewardPoints);
        ReferralBalance++;
        UpdatedAt = DateTime.UtcNow;

        return LoyaltyTransaction.ReferralPointsEarned(this, referral, credited, validatedBy);
    }

    // BUSINESS.md §5.4: when the patient's cycle has ended, the active points
    // balance and referral progress reset to zero for the next cycle; the
    // returned CycleReset transaction records it. Called only by
    // LoyaltyCycleLifecycle.
    internal LoyaltyTransaction ResetForEndedCycle(LoyaltyCycle endedCycle)
    {
        if (endedCycle.PatientId != PatientId || endedCycle.ClinicId != ClinicId)
        {
            throw new DomainException(
                "The loyalty cycle does not belong to this loyalty account's patient.");
        }

        var transaction = LoyaltyTransaction.CycleReset(this, endedCycle, -Points, -ReferralBalance);

        Points = 0;
        ReferralBalance = 0;
        UpdatedAt = DateTime.UtcNow;

        return transaction;
    }

    private int CreditPoints(int points)
    {
        var credited = Math.Min(points, MaximumPointsBalance - Points);

        if (credited > 0)
        {
            Points += credited;
            UpdatedAt = DateTime.UtcNow;
        }

        return credited;
    }
}
