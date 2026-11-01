using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;

namespace DentalLoyalty.Domain.Entities;

// A patient's individual loyalty cycle (BUSINESS.md §5.1; DATA.md §14). A patient
// has no cycle - and no loyalty benefits - until they have registered in the
// Patient App and their user account is linked to their Patient record; the
// first cycle starts at that moment. A Patient may later have several historical
// cycles, but only one active cycle at a time (enforced by the database).
//
// A cycle ends exactly once, by completion or expiry (§5.2, §5.3), and the next
// one starts at that end (§5.4) - see LoyaltyCycleLifecycle, the only code that
// ends cycles and starts subsequent ones. Ended cycles are kept as history.
public class LoyaltyCycle
{
    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid PatientId { get; private set; }

    public LoyaltyCycleStatus Status { get; private set; }

    public DateTime StartedAt { get; private set; }

    // BUSINESS.md §5.1/§5.3: a cycle lasts one year and, unless successfully
    // completed first, expires one year after its start date.
    public DateTime ExpiresAt => StartedAt.AddYears(1);

    // When the cycle ended: the moment of completion, or ExpiresAt for an
    // expired cycle (§5.3). Null while active.
    public DateTime? EndedAt { get; private set; }

    private LoyaltyCycle()
    {
    }

    public bool HasExpiredAt(DateTime at) => at >= ExpiresAt;

    // Whether a recorded service earns points in this cycle (BUSINESS.md §3.1,
    // §5.1, §5.3): the cycle is this patient's active cycle, the service was
    // performed after the cycle started - nothing before registration and
    // linking is credited retroactively - and the cycle had not expired when the
    // service was recorded, so points never go to an expired cycle.
    public bool AcceptsService(PatientService patientService)
    {
        ArgumentNullException.ThrowIfNull(patientService);

        return Status == LoyaltyCycleStatus.Active
            && patientService.PatientId == PatientId
            && patientService.ClinicId == ClinicId
            && patientService.UsedAt >= StartedAt
            && !HasExpiredAt(patientService.RecordedAt);
    }

    // Whether the patient was registered and linked, with this cycle running, at
    // the given moment (BUSINESS.md §4.2, §5.1): a referral that became valid
    // before the cycle started - before registration and linking - generates
    // nothing, even after the patient registers.
    public bool WasRunningAt(DateTime at) =>
        Status == LoyaltyCycleStatus.Active
        && at >= StartedAt
        && !HasExpiredAt(at);

    // BUSINESS.md §5.2: the cycle is completed when, at the given time, it is
    // still running, the patient has reached the 1,000,000-point target and five
    // referrals in it, and has redeemed every reward unlocked in it. Balances are
    // the cycle's own (they reset with it, §5.4) and are never reduced within it
    // (§13), so a reward unlocked during the cycle is still unlocked now; one
    // the patient can no longer redeem because the clinic withdrew it does not
    // block completion (ADR-008). redeemedRewardIds are the rewards redeemed in
    // this cycle.
    public bool IsCompletedBy(
        LoyaltyAccount account,
        IEnumerable<Reward> clinicRewards,
        IReadOnlySet<Guid> redeemedRewardIds,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(clinicRewards);
        ArgumentNullException.ThrowIfNull(redeemedRewardIds);

        return Status == LoyaltyCycleStatus.Active
            && account.PatientId == PatientId
            && account.ClinicId == ClinicId
            && !HasExpiredAt(at)
            && account.Points >= LoyaltyAccount.MaximumPointsBalance
            && account.ReferralBalance >= LoyaltyAccount.MaximumReferralRewards
            && !clinicRewards.Any(r =>
                r.StatusFor(account, this, redeemedRewardIds.Contains(r.Id), at) == RewardStatus.Available);
    }

    internal void End(LoyaltyCycleStatus status, DateTime endedAt)
    {
        if (Status != LoyaltyCycleStatus.Active)
        {
            throw new DomainException("Only an active loyalty cycle can end.");
        }

        if (status is not (LoyaltyCycleStatus.Completed or LoyaltyCycleStatus.Expired))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A cycle ends by completion or expiry.");
        }

        Status = status;
        EndedAt = endedAt;
    }

    // BUSINESS.md §5.4: the next cycle starts on the actual end of the previous one.
    internal static LoyaltyCycle StartNext(LoyaltyCycle previous)
    {
        if (previous.EndedAt is not DateTime endedAt)
        {
            throw new DomainException("The next loyalty cycle starts only when the previous one has ended.");
        }

        return new LoyaltyCycle
        {
            Id = Guid.NewGuid(),
            ClinicId = previous.ClinicId,
            PatientId = previous.PatientId,
            Status = LoyaltyCycleStatus.Active,
            StartedAt = endedAt,
        };
    }

    // BUSINESS.md §5.1: the first cycle starts when registration and account
    // linking are successfully completed. Callers persist it in the same
    // operation as the link, so the cycle exists exactly when the link does.
    public static LoyaltyCycle StartFirst(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);

        if (patient.UserId is null)
        {
            throw new DomainException(
                "A loyalty cycle can start only after the patient has registered and linked their user account.");
        }

        return new LoyaltyCycle
        {
            Id = Guid.NewGuid(),
            ClinicId = patient.ClinicId,
            PatientId = patient.Id,
            Status = LoyaltyCycleStatus.Active,
            StartedAt = DateTime.UtcNow,
        };
    }
}
