using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Loyalty;
using DentalLoyalty.UnitTests.TestDoubles;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

// The one-year expiry boundary (BUSINESS.md §5.2-§5.4; ADR-008) for the
// operations that can complete a cycle by earning: recording a service and
// validating a referral. A cycle runs on [StartedAt, ExpiresAt): at ExpiresAt it
// has expired.
//
// Each test runs the sequence the handler runs - bring the cycle up to date
// (LoyaltyCycleLifecycle.RollOverExpired), record the activity, credit it,
// evaluate completion at the earning transaction's time - at one explicit
// instant, so the boundary is tested exactly: one tick before, exactly at and
// one tick after ExpiresAt. The cycle started two years ago, so every instant is
// in the past.
public class CycleExpiryBoundaryTests
{
    private static readonly Guid AdminUserId = Guid.NewGuid();

    private static readonly Guid ClinicId = Guid.NewGuid();

    private static readonly DateTime CycleStart = DateTime.UtcNow.AddYears(-2);

    private static readonly DateTime ExpiresAt = CycleStart.AddYears(1);

    private static readonly LoyaltyConfiguration Scoring = LoyaltyConfiguration.CreateDefault(ClinicId);

    // 7,500 RSD = 75,000 points with the default scoring configuration.
    private static readonly Service Service = new(ClinicId, "Usluga 7500", 7_500m);

    private static Patient LinkedPatient(string name)
    {
        var patient = new Patient(ClinicId, name, "TestPatient", "+381600000000");
        patient.LinkUser(Guid.NewGuid());
        return patient;
    }

    private sealed record Member(Patient Patient, LoyaltyAccount Account, LoyaltyCycle FirstCycle);

    private static Member NewMember(string name, int points, int referrals)
    {
        var patient = LinkedPatient(name);
        var account = LoyaltyAccount.OpenFor(patient).WithBalances(points, referrals);
        var cycle = LoyaltyCycle.StartFirst(patient).StartedOn(CycleStart);
        return new Member(patient, account, cycle);
    }

    // The cycle as of the operation's instant, ending it first if it expired.
    private static LoyaltyCycle CurrentCycleAt(Member member, DateTime at)
    {
        var transitions = LoyaltyCycleLifecycle.RollOverExpired(member.Account, member.FirstCycle, at);
        return transitions.Count == 0 ? member.FirstCycle : transitions[^1].Next;
    }

    private static CycleTransition? CompleteAt(Member member, LoyaltyCycle current, DateTime at) =>
        LoyaltyCycleLifecycle.CompleteIfSatisfied(member.Account, current, [], new HashSet<Guid>(), at);

    // --- service --------------------------------------------------------------

    private sealed record ServiceOutcome(
        LoyaltyCycle Current,
        PatientService Service,
        LoyaltyTransaction? Earned,
        CycleTransition? Completion);

    // As RecordPatientServiceHandler: one instant for the cycle, the recording,
    // the credit and the completion.
    private static ServiceOutcome RecordService(Member member, DateTime performedAt, DateTime at)
    {
        var current = CurrentCycleAt(member, at);
        var patientService = new PatientService(Guid.NewGuid(), member.Patient, Service, Scoring, current, performedAt, AdminUserId, at);

        if (!current.AcceptsService(patientService))
        {
            return new ServiceOutcome(current, patientService, null, null);
        }

        var earned = member.Account.EarnServicePoints(patientService, current);
        return new ServiceOutcome(current, patientService, earned, CompleteAt(member, current, earned.CreatedAt));
    }

    // At 950,000 points and 5 referrals, the next service reaches the target.
    private static Member AtServiceThreshold() => NewMember("Ana", 950_000, 5);

    [Fact]
    public void Service_ImmediatelyBeforeExpiry_CompletesTheCycleAtThatInstant()
    {
        var member = AtServiceThreshold();
        var at = ExpiresAt.AddTicks(-1);

        var outcome = RecordService(member, at, at);

        Assert.Same(member.FirstCycle, outcome.Current);
        Assert.Equal(at, outcome.Service.RecordedAt);
        Assert.Equal((50_000, at), (outcome.Earned!.PointsChange, outcome.Earned.CreatedAt));
        Assert.NotNull(outcome.Completion);
        Assert.Equal((LoyaltyCycleStatus.Completed, (DateTime?)at), (member.FirstCycle.Status, member.FirstCycle.EndedAt));
        Assert.Equal(at, outcome.Completion!.Next.StartedAt);
        Assert.Equal((at, -1_000_000, -5), (outcome.Completion.Reset.CreatedAt, outcome.Completion.Reset.PointsChange, outcome.Completion.Reset.ReferralChange));
    }

    // At ExpiresAt the cycle has already expired (§5.3): it ends as Expired at
    // ExpiresAt, never Completed, and the service earns in the next cycle.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Service_AtOrAfterExpiry_ExpiresTheCycle_AndEarnsInTheNext(long ticksAfterExpiry)
    {
        var member = AtServiceThreshold();
        var at = ExpiresAt.AddTicks(ticksAfterExpiry);

        var outcome = RecordService(member, at, at);

        Assert.Equal((LoyaltyCycleStatus.Expired, (DateTime?)ExpiresAt), (member.FirstCycle.Status, member.FirstCycle.EndedAt));
        Assert.Equal((LoyaltyCycleStatus.Active, ExpiresAt), (outcome.Current.Status, outcome.Current.StartedAt));
        Assert.Equal((75_000, at), (outcome.Earned!.PointsChange, outcome.Earned.CreatedAt));
        Assert.Null(outcome.Completion);
        Assert.Equal((75_000, 0), (member.Account.Points, member.Account.ReferralBalance));
    }

    // ADR-008 decision 1: a service performed before the boundary but recorded
    // after it is recorded without points - it belongs to the expired cycle,
    // which can no longer complete.
    [Fact]
    public void Service_PerformedBeforeExpiry_RecordedAfter_EarnsNothing_AndTheCycleExpires()
    {
        var member = AtServiceThreshold();

        var outcome = RecordService(member, ExpiresAt.AddTicks(-1), ExpiresAt.AddTicks(1));

        Assert.Equal(LoyaltyCycleStatus.Expired, member.FirstCycle.Status);
        Assert.Equal(0, outcome.Service.EarnedPoints);
        Assert.Null(outcome.Earned);
        Assert.Equal((0, 0), (member.Account.Points, member.Account.ReferralBalance));
    }

    // --- referral -------------------------------------------------------------

    private sealed record ReferralOutcome(
        LoyaltyCycle ReferrerCycle,
        Referral Referral,
        ReferralValidation Validation,
        LoyaltyTransaction? ReferrerEarned,
        CycleTransition? Completion);

    // As ValidateReferralHandler: one instant for both cycles, the validation,
    // the credits and the referrer's completion. Both patients registered when
    // their first cycles started.
    private static ReferralOutcome ValidateReferral(Member referrer, Member referred, DateTime examinedAt, DateTime at)
    {
        var referrerCycle = CurrentCycleAt(referrer, at);
        var referredCycle = CurrentCycleAt(referred, at);
        var referral = Referral.Record(referrer.Patient, referred.Patient, AdminUserId);

        var validation = referral.Validate(
            new ReferralParticipant(referrer.Account, referrerCycle, CycleStart),
            new ReferralParticipant(referred.Account, referredCycle, CycleStart),
            examinedAt,
            AdminUserId,
            at);

        var earned = validation.Transactions.SingleOrDefault(t => t.PatientId == referrer.Patient.Id);
        var completion = validation.Referrer == ReferralRewardOutcome.Rewarded
            ? CompleteAt(referrer, referrerCycle, earned!.CreatedAt)
            : null;

        return new ReferralOutcome(referrerCycle, referral, validation, earned, completion);
    }

    // At 1,000,000 points and 4 referrals, the fifth referral reaches the target.
    private static (Member Referrer, Member Referred) AtReferralThreshold() =>
        (NewMember("Ana", 1_000_000, 4), NewMember("Marko", 0, 0));

    [Fact]
    public void Referral_ImmediatelyBeforeExpiry_CompletesTheReferrersCycleAtThatInstant()
    {
        var (referrer, referred) = AtReferralThreshold();
        var at = ExpiresAt.AddTicks(-1);

        var outcome = ValidateReferral(referrer, referred, at, at);

        Assert.Equal(ReferralRewardOutcome.Rewarded, outcome.Validation.Referrer);
        Assert.Equal((DateTime?)at, outcome.Referral.CompletedAt);
        Assert.Equal((0, 1, at), (outcome.ReferrerEarned!.PointsChange, outcome.ReferrerEarned.ReferralChange, outcome.ReferrerEarned.CreatedAt));
        Assert.NotNull(outcome.Completion);
        Assert.Equal((LoyaltyCycleStatus.Completed, (DateTime?)at), (referrer.FirstCycle.Status, referrer.FirstCycle.EndedAt));
        Assert.Equal((at, at), (outcome.Completion!.Next.StartedAt, outcome.Completion.Reset.CreatedAt));
    }

    // Examined and validated at or after the boundary: the referrer's cycle has
    // expired - never Completed - and the referral rewards them in the next one.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Referral_AtOrAfterExpiry_ExpiresTheCycle_AndRewardsInTheNext(long ticksAfterExpiry)
    {
        var (referrer, referred) = AtReferralThreshold();
        var at = ExpiresAt.AddTicks(ticksAfterExpiry);

        var outcome = ValidateReferral(referrer, referred, at, at);

        Assert.Equal((LoyaltyCycleStatus.Expired, (DateTime?)ExpiresAt), (referrer.FirstCycle.Status, referrer.FirstCycle.EndedAt));
        Assert.Equal(ExpiresAt, outcome.ReferrerCycle.StartedAt);
        Assert.Equal(ReferralRewardOutcome.Rewarded, outcome.Validation.Referrer);
        Assert.Equal((50_000, 1, at), (outcome.ReferrerEarned!.PointsChange, outcome.ReferrerEarned.ReferralChange, outcome.ReferrerEarned.CreatedAt));
        Assert.Null(outcome.Completion);
        Assert.Equal((50_000, 1), (referrer.Account.Points, referrer.Account.ReferralBalance));
    }

    // ADR-008 decision 1: examined before the boundary, validated after it -
    // the referral belongs to the expired cycle, so the referrer gets nothing
    // and that cycle cannot complete.
    [Fact]
    public void Referral_ExaminedBeforeExpiry_ValidatedAfter_RewardsNothing_AndTheCycleExpires()
    {
        var (referrer, referred) = AtReferralThreshold();

        var outcome = ValidateReferral(referrer, referred, ExpiresAt.AddTicks(-1), ExpiresAt.AddTicks(1));

        Assert.Equal(ReferralRewardOutcome.CycleEnded, outcome.Validation.Referrer);
        Assert.Null(outcome.ReferrerEarned);
        Assert.Null(outcome.Completion);
        Assert.Equal(LoyaltyCycleStatus.Expired, referrer.FirstCycle.Status);
        Assert.Equal((0, 0), (referrer.Account.Points, referrer.Account.ReferralBalance));
    }

    // --- redemption -----------------------------------------------------------

    // At both targets, with the Tier 3 reward the only one left to redeem.
    private static (Member Member, Reward Reward) AtRedemptionThreshold() =>
        (NewMember("Ana", 1_000_000, 5), new Reward(ClinicId, "Kućno izbeljivanje zuba", 1_000_000, 5));

    // As RedeemRewardHandler: one instant for the cycle, the availability, the
    // redemption and the completion it causes.
    [Fact]
    public void Redemption_ImmediatelyBeforeExpiry_CompletesTheCycleAtThatInstant()
    {
        var (member, reward) = AtRedemptionThreshold();
        var at = ExpiresAt.AddTicks(-1);
        var current = CurrentCycleAt(member, at);

        var (redemption, transaction) = member.Account.RedeemReward(reward, current, alreadyRedeemedInCycle: false, AdminUserId, at);
        var completion = LoyaltyCycleLifecycle.CompleteIfSatisfied(
            member.Account, current, [reward], new HashSet<Guid> { reward.Id }, redemption.RedeemedAt);

        Assert.Equal((at, at), (redemption.RedeemedAt, transaction.CreatedAt));
        Assert.Equal((LoyaltyCycleStatus.Completed, (DateTime?)at), (member.FirstCycle.Status, member.FirstCycle.EndedAt));
        Assert.Equal((at, at), (completion!.Next.StartedAt, completion.Reset.CreatedAt));
    }

    // At or after ExpiresAt the cycle has expired and its progress reset, so
    // the reward is no longer available and cannot be redeemed.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Redemption_AtOrAfterExpiry_TheCycleExpires_AndTheRewardIsLocked(long ticksAfterExpiry)
    {
        var (member, reward) = AtRedemptionThreshold();
        var at = ExpiresAt.AddTicks(ticksAfterExpiry);
        var current = CurrentCycleAt(member, at);

        Assert.Equal((LoyaltyCycleStatus.Expired, (DateTime?)ExpiresAt), (member.FirstCycle.Status, member.FirstCycle.EndedAt));
        Assert.Equal(RewardStatus.Locked, reward.StatusFor(member.Account, current, redeemedInCycle: false, at));
        Assert.Throws<DentalLoyalty.Domain.Exceptions.DomainException>(() =>
            member.Account.RedeemReward(reward, current, alreadyRedeemedInCycle: false, AdminUserId, at));
    }

    // --- recording time guards --------------------------------------------------

    [Fact]
    public void RecordingOrValidationTime_InTheFuture_OrNotUtc_IsRejected()
    {
        var member = NewMember("Ana", 0, 0);
        var future = DateTime.UtcNow.AddMinutes(5);
        var local = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Local);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PatientService(Guid.NewGuid(), member.Patient, Service, Scoring, member.FirstCycle, DateTime.UtcNow, AdminUserId, future));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PatientService(Guid.NewGuid(), member.Patient, Service, Scoring, member.FirstCycle, DateTime.UtcNow.AddMinutes(-10), AdminUserId, local));

        var reward = new Reward(ClinicId, "Free", 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            member.Account.RedeemReward(reward, member.FirstCycle, alreadyRedeemedInCycle: false, AdminUserId, future));

        var other = NewMember("Marko", 0, 0);
        Assert.Throws<DentalLoyalty.Domain.Exceptions.DomainException>(() =>
            Referral.Record(member.Patient, other.Patient, AdminUserId).Validate(
                new ReferralParticipant(member.Account, member.FirstCycle, CycleStart),
                new ReferralParticipant(other.Account, other.FirstCycle, CycleStart),
                DateTime.UtcNow,
                AdminUserId,
                future));
    }
}
