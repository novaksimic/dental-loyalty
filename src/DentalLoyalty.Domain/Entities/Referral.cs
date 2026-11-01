using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;

namespace DentalLoyalty.Domain.Entities;

// An existing patient's recommendation of a new patient to the same clinic
// (BUSINESS.md §4; DATA.md §10), recorded by a Clinic Admin (ADR-007). It stays
// Pending until the referred patient's dental examination makes it valid
// (§4.1); Validate then records that moment and the referral rewards it
// generates (§4.2). A referral and its outcome are kept for history even when
// they generate no loyalty benefits.
public class Referral
{
    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid ReferrerPatientId { get; private set; }

    public Guid ReferredPatientId { get; private set; }

    public ReferralStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // The Clinic Admin who recorded the referral.
    public Guid CreatedBy { get; private set; }

    // When the referral was validated (Status Completed).
    public DateTime? CompletedAt { get; private set; }

    // When the referred patient completed the examination - the moment the
    // referral became valid (BUSINESS.md §4.1), against which the registration
    // and account-linking requirement is evaluated (§4.2).
    public DateTime? ExaminedAt { get; private set; }

    // The Clinic Admin who confirmed the examination.
    public Guid? ValidatedBy { get; private set; }

    // BUSINESS.md §4.2: true when either patient was not registered and linked
    // when the referral became valid. Neither patient then received anything,
    // and never will for this referral.
    public bool RewardsForfeited { get; private set; }

    // Optimistic concurrency token (as ADR-005 for accounts): two validations of
    // the same referral cannot both be saved.
    public byte[] RowVersion { get; private set; } = [];

    private Referral()
    {
    }

    // DATA.md §10: both patients belong to the same clinic, and a patient cannot
    // refer themselves.
    public static Referral Record(Patient referrer, Patient referred, Guid createdBy)
    {
        ArgumentNullException.ThrowIfNull(referrer);
        ArgumentNullException.ThrowIfNull(referred);

        if (createdBy == Guid.Empty)
        {
            throw new ArgumentException("The user recording the referral is required.", nameof(createdBy));
        }

        if (referrer.ClinicId != referred.ClinicId)
        {
            throw new DomainException("A referral must be between patients of the same clinic.");
        }

        if (referrer.Id == referred.Id)
        {
            throw new DomainException("A patient cannot refer themselves.");
        }

        return new Referral
        {
            Id = Guid.NewGuid(),
            ClinicId = referrer.ClinicId,
            ReferrerPatientId = referrer.Id,
            ReferredPatientId = referred.Id,
            Status = ReferralStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };
    }

    // Records that the referred patient completed the examination at examinedAt,
    // which makes the referral valid (BUSINESS.md §4.1), and applies its referral
    // rewards (§4.2; ADR-007, ADR-008).
    //
    // Rewards are generated only if both patients were registered and linked at
    // examinedAt - their first cycle had started (ReferralParticipant
    // .RegisteredAt); otherwise they are forfeited for both, permanently. When
    // they are generated, each patient is credited independently: only if their
    // current cycle was already running at examinedAt - an examination in a
    // cycle that has since ended is not credited to the next one (ADR-008) - and
    // up to their own per-cycle maximum (LoyaltyAccount.EarnReferralReward).
    public ReferralValidation Validate(
        ReferralParticipant referrer,
        ReferralParticipant referred,
        DateTime examinedAt,
        Guid validatedBy)
    {
        ArgumentNullException.ThrowIfNull(referrer);
        ArgumentNullException.ThrowIfNull(referred);

        if (validatedBy == Guid.Empty)
        {
            throw new ArgumentException("The user confirming the examination is required.", nameof(validatedBy));
        }

        if (Status != ReferralStatus.Pending)
        {
            throw new DomainException("Only a pending referral can be validated.");
        }

        if (referrer.Account.PatientId != ReferrerPatientId
            || referred.Account.PatientId != ReferredPatientId
            || referrer.Account.ClinicId != ClinicId
            || referred.Account.ClinicId != ClinicId)
        {
            throw new DomainException("The loyalty accounts do not belong to this referral's patients.");
        }

        var now = DateTime.UtcNow;

        if (examinedAt.Kind != DateTimeKind.Utc || examinedAt > now)
        {
            throw new DomainException("The examination time must be a UTC time that is not in the future.");
        }

        Status = ReferralStatus.Completed;
        CompletedAt = now;
        ExaminedAt = examinedAt;
        ValidatedBy = validatedBy;

        if (!WasRegisteredAt(referrer, examinedAt) || !WasRegisteredAt(referred, examinedAt))
        {
            RewardsForfeited = true;
            return new ReferralValidation([], ReferralRewardOutcome.Forfeited, ReferralRewardOutcome.Forfeited);
        }

        var transactions = new List<LoyaltyTransaction>(2);
        var referrerOutcome = Reward(referrer, examinedAt, validatedBy, transactions);
        var referredOutcome = Reward(referred, examinedAt, validatedBy, transactions);

        return new ReferralValidation(transactions, referrerOutcome, referredOutcome);
    }

    private bool WasRegisteredAt(ReferralParticipant participant, DateTime examinedAt) =>
        participant.CurrentCycle is { } cycle
        && cycle.PatientId == participant.Account.PatientId
        && cycle.ClinicId == ClinicId
        && participant.RegisteredAt is { } registeredAt
        && registeredAt <= examinedAt;

    private ReferralRewardOutcome Reward(
        ReferralParticipant participant,
        DateTime examinedAt,
        Guid validatedBy,
        List<LoyaltyTransaction> transactions)
    {
        if (!participant.CurrentCycle!.WasRunningAt(examinedAt))
        {
            return ReferralRewardOutcome.CycleEnded;
        }

        if (participant.Account.EarnReferralReward(this, validatedBy) is not { } transaction)
        {
            return ReferralRewardOutcome.MaximumReached;
        }

        transactions.Add(transaction);
        return ReferralRewardOutcome.Rewarded;
    }
}

// One patient of a referral being validated: their account, their current
// (active) cycle - null when they have none - and when they registered and
// linked their account, which is when their first cycle started (BUSINESS.md
// §5.1); null when they have not.
public sealed record ReferralParticipant(
    LoyaltyAccount Account,
    LoyaltyCycle? CurrentCycle,
    DateTime? RegisteredAt);

// The ReferralPointsEarned transactions of the patients who received a referral
// reward - none, one or both - and what each patient received.
public sealed record ReferralValidation(
    IReadOnlyList<LoyaltyTransaction> Transactions,
    ReferralRewardOutcome Referrer,
    ReferralRewardOutcome Referred);
