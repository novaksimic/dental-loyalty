using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

// Referral recording and validation (BUSINESS.md §4, §5.1; ADR-007) on the domain
// model: Referral.Record / Validate, LoyaltyAccount.EarnReferralReward and
// LoyaltyCycle.WasRunningAt.
public class ReferralTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();

    private readonly Patient _referrer;
    private readonly Patient _referred;

    public ReferralTests()
    {
        _referrer = new Patient(ClinicId, "Ana", "Petrovic", "+381600000000");
        _referred = new Patient(ClinicId, "Marko", "Jovanovic", "+381600000001");
    }

    private static LoyaltyCycle Register(Patient patient, DateTime? startedAt = null)
    {
        patient.LinkUser(Guid.NewGuid());
        var cycle = LoyaltyCycle.StartFirst(patient);
        return startedAt is null ? cycle : cycle.StartedOn(startedAt.Value);
    }

    private static LoyaltyAccount Account(Patient patient, int points = 0, int referrals = 0) =>
        LoyaltyAccount.OpenFor(patient).WithBalances(points, referrals);

    private Referral NewReferral() => Referral.Record(_referrer, _referred, AdminUserId);

    // A patient taking part in the referral, registered when their (first)
    // cycle started.
    private static ReferralParticipant P(LoyaltyAccount account, LoyaltyCycle? cycle) =>
        new(account, cycle, cycle?.StartedAt);

    // --- recording ------------------------------------------------------------

    [Fact]
    public void Record_CreatesPendingReferralInThePatientsClinic()
    {
        var referral = NewReferral();

        Assert.Equal(ReferralStatus.Pending, referral.Status);
        Assert.Equal(ClinicId, referral.ClinicId);
        Assert.Equal(_referrer.Id, referral.ReferrerPatientId);
        Assert.Equal(_referred.Id, referral.ReferredPatientId);
        Assert.Equal(AdminUserId, referral.CreatedBy);
        Assert.Null(referral.CompletedAt);
        Assert.Null(referral.ExaminedAt);
        Assert.False(referral.RewardsForfeited);
    }

    [Fact]
    public void Record_SelfReferral_IsRejected()
    {
        Assert.Throws<DomainException>(() => Referral.Record(_referrer, _referrer, AdminUserId));
    }

    [Fact]
    public void Record_PatientsOfDifferentClinics_IsRejected()
    {
        var other = new Patient(Guid.NewGuid(), "Iva", "Ilic", "+381600000002");

        Assert.Throws<DomainException>(() => Referral.Record(_referrer, other, AdminUserId));
    }

    [Fact]
    public void Record_WithoutRecordingUser_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Referral.Record(_referrer, _referred, Guid.Empty));
    }

    // --- validation: rewards --------------------------------------------------

    // BUSINESS.md §4.2; ADR-007 §2: both patients get 50,000 points and one credit.
    [Fact]
    public void Validate_BothRegistered_RewardsBothWithPointsAndOneCredit()
    {
        var referrerCycle = Register(_referrer);
        var referredCycle = Register(_referred);
        var referrerAccount = Account(_referrer, 100_000, 2);
        var referredAccount = Account(_referred);
        var referral = NewReferral();
        var examinedAt = DateTime.UtcNow;

        var transactions = referral.Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), examinedAt, AdminUserId).Transactions;

        Assert.Equal((150_000, 3), (referrerAccount.Points, referrerAccount.ReferralBalance));
        Assert.Equal((50_000, 1), (referredAccount.Points, referredAccount.ReferralBalance));
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, t =>
        {
            Assert.Equal(LoyaltyTransactionType.ReferralPointsEarned, t.Type);
            Assert.Equal(50_000, t.PointsChange);
            Assert.Equal(1, t.ReferralChange);
            Assert.Equal(referral.Id, t.ReferralId);
            Assert.Equal(AdminUserId, t.CreatedBy);
            Assert.Equal(ClinicId, t.ClinicId);
        });
        Assert.Equal(new[] { _referrer.Id, _referred.Id }, transactions.Select(t => t.PatientId));

        Assert.Equal(ReferralStatus.Completed, referral.Status);
        Assert.Equal(examinedAt, referral.ExaminedAt);
        Assert.Equal(AdminUserId, referral.ValidatedBy);
        Assert.NotNull(referral.CompletedAt);
        Assert.False(referral.RewardsForfeited);
    }

    // BUSINESS.md §3.3: only the points that fit are credited; the credit still is.
    [Fact]
    public void Validate_NearTheMaximumBalance_CreditsOnlyWhatFits()
    {
        var referrerCycle = Register(_referrer);
        var referredCycle = Register(_referred);
        var referrerAccount = Account(_referrer, 980_000, 1);
        var referredAccount = Account(_referred);

        var transactions = NewReferral().Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), DateTime.UtcNow, AdminUserId).Transactions;

        Assert.Equal((1_000_000, 2), (referrerAccount.Points, referrerAccount.ReferralBalance));
        Assert.Equal(20_000, transactions.Single(t => t.PatientId == _referrer.Id).PointsChange);
    }

    // ADR-007 §1/§3: the per-cycle maximum applies to each patient independently.
    [Fact]
    public void Validate_ReferrerAtTheMaximum_RewardsOnlyTheReferred()
    {
        var referrerCycle = Register(_referrer);
        var referredCycle = Register(_referred);
        var referrerAccount = Account(_referrer, 400_000, LoyaltyAccount.MaximumReferralRewards);
        var referredAccount = Account(_referred);

        var transactions = NewReferral().Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), DateTime.UtcNow, AdminUserId).Transactions;

        Assert.Equal((400_000, 5), (referrerAccount.Points, referrerAccount.ReferralBalance));
        Assert.Null(referrerAccount.UpdatedAt);
        Assert.Equal((50_000, 1), (referredAccount.Points, referredAccount.ReferralBalance));
        Assert.Equal(_referred.Id, Assert.Single(transactions).PatientId);
    }

    [Fact]
    public void Validate_FourthToFifthReward_IsStillCredited()
    {
        var referrerCycle = Register(_referrer);
        var referredCycle = Register(_referred);
        var referrerAccount = Account(_referrer, 0, LoyaltyAccount.MaximumReferralRewards - 1);
        var referredAccount = Account(_referred);

        NewReferral().Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), DateTime.UtcNow, AdminUserId);

        Assert.Equal(5, referrerAccount.ReferralBalance);
    }

    // --- validation: registration and linking (BUSINESS.md §4.2, §5.1) --------

    [Fact]
    public void Validate_ReferredNotRegistered_ForfeitsForBoth()
    {
        var referrerCycle = Register(_referrer);
        var referrerAccount = Account(_referrer, 100_000, 1);
        var referredAccount = Account(_referred);
        var referral = NewReferral();

        var transactions = referral.Validate(P(referrerAccount, referrerCycle), P(referredAccount, null), DateTime.UtcNow, AdminUserId).Transactions;

        Assert.Empty(transactions);
        Assert.Equal((100_000, 1), (referrerAccount.Points, referrerAccount.ReferralBalance));
        Assert.Equal((0, 0), (referredAccount.Points, referredAccount.ReferralBalance));
        Assert.Equal(ReferralStatus.Completed, referral.Status);
        Assert.True(referral.RewardsForfeited);
    }

    [Fact]
    public void Validate_ReferrerNotRegistered_ForfeitsForBoth()
    {
        var referredCycle = Register(_referred);
        var referrerAccount = Account(_referrer);
        var referredAccount = Account(_referred);
        var referral = NewReferral();

        var transactions = referral.Validate(P(referrerAccount, null), P(referredAccount, referredCycle), DateTime.UtcNow, AdminUserId).Transactions;

        Assert.Empty(transactions);
        Assert.Equal(0, referredAccount.Points);
        Assert.True(referral.RewardsForfeited);
    }

    // Registering after the examination does not grant the rewards retroactively.
    [Fact]
    public void Validate_ExaminationBeforeAPatientRegistered_ForfeitsForBoth()
    {
        var referrerCycle = Register(_referrer, DateTime.UtcNow.AddDays(-30));
        var referredCycle = Register(_referred, DateTime.UtcNow.AddDays(-1));
        var referrerAccount = Account(_referrer);
        var referredAccount = Account(_referred);
        var referral = NewReferral();

        var transactions = referral.Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), DateTime.UtcNow.AddDays(-2), AdminUserId).Transactions;

        Assert.Empty(transactions);
        Assert.Equal((0, 0), (referrerAccount.Points, referredAccount.Points));
        Assert.True(referral.RewardsForfeited);
    }

    [Fact]
    public void Validate_ExaminationAfterBothRegistered_Rewards()
    {
        var referrerCycle = Register(_referrer, DateTime.UtcNow.AddDays(-30));
        var referredCycle = Register(_referred, DateTime.UtcNow.AddDays(-3));
        var referrerAccount = Account(_referrer);
        var referredAccount = Account(_referred);

        var transactions = NewReferral().Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), DateTime.UtcNow.AddDays(-2), AdminUserId).Transactions;

        Assert.Equal(2, transactions.Count);
    }

    // A cycle of another patient is not the patient's registration.
    [Fact]
    public void Validate_WithAnotherPatientsCycle_ForfeitsForBoth()
    {
        var referrerCycle = Register(_referrer);
        Register(_referred);
        var referral = NewReferral();

        var transactions = referral.Validate(P(Account(_referrer), referrerCycle), P(Account(_referred), referrerCycle), DateTime.UtcNow, AdminUserId).Transactions;

        Assert.Empty(transactions);
        Assert.True(referral.RewardsForfeited);
    }

    // --- validation: invalid --------------------------------------------------

    [Fact]
    public void Validate_Twice_IsRejected_AndRewardsOnlyOnce()
    {
        var referrerCycle = Register(_referrer);
        var referredCycle = Register(_referred);
        var referrerAccount = Account(_referrer);
        var referredAccount = Account(_referred);
        var referral = NewReferral();
        referral.Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), DateTime.UtcNow, AdminUserId);

        Assert.Throws<DomainException>(() =>
            referral.Validate(P(referrerAccount, referrerCycle), P(referredAccount, referredCycle), DateTime.UtcNow, AdminUserId));

        Assert.Equal((50_000, 1), (referrerAccount.Points, referrerAccount.ReferralBalance));
    }

    [Fact]
    public void Validate_ExaminationInTheFuture_IsRejected_AndChangesNothing()
    {
        var referrerCycle = Register(_referrer);
        var referredCycle = Register(_referred);
        var referrerAccount = Account(_referrer);
        var referral = NewReferral();

        Assert.Throws<DomainException>(() =>
            referral.Validate(P(referrerAccount, referrerCycle), P(Account(_referred), referredCycle), DateTime.UtcNow.AddHours(1), AdminUserId));

        Assert.Equal(ReferralStatus.Pending, referral.Status);
        Assert.Equal(0, referrerAccount.Points);
    }

    [Fact]
    public void Validate_WithAnotherPatientsAccount_IsRejected()
    {
        var referrerCycle = Register(_referrer);
        var referredCycle = Register(_referred);
        var referral = NewReferral();

        Assert.Throws<DomainException>(() =>
            referral.Validate(P(Account(_referred), referrerCycle), P(Account(_referrer), referredCycle), DateTime.UtcNow, AdminUserId));

        Assert.Equal(ReferralStatus.Pending, referral.Status);
    }

    [Fact]
    public void Validate_WithoutConfirmingUser_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            NewReferral().Validate(P(Account(_referrer), null), P(Account(_referred), null), DateTime.UtcNow, Guid.Empty));
    }

    // --- LoyaltyCycle.WasRunningAt ---------------------------------------------

    [Fact]
    public void WasRunningAt_IsFalseBeforeTheStartAndFromTheExpiry()
    {
        var start = DateTime.UtcNow.AddMonths(-13);
        var cycle = Register(_referrer, start);

        Assert.False(cycle.WasRunningAt(start.AddTicks(-1)));
        Assert.True(cycle.WasRunningAt(start));
        Assert.True(cycle.WasRunningAt(start.AddYears(1).AddTicks(-1)));
        Assert.False(cycle.WasRunningAt(start.AddYears(1)));
    }
}
