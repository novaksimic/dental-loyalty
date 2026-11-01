using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

// Reward eligibility (BUSINESS.md §9-§12) and redemption (§13, §19, §20) on the
// domain model: Reward.IsUnlockedBy / StatusFor and LoyaltyAccount.RedeemReward.
public class RewardRedemptionTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();

    private readonly Patient _patient;

    private readonly LoyaltyCycle _cycle;

    // A registered, linked patient with an active first cycle (BUSINESS.md §5.1).
    public RewardRedemptionTests()
    {
        _patient = new Patient(ClinicId, "Ana", "Petrovic", "+381600000000");
        _patient.LinkUser(Guid.NewGuid());
        _cycle = LoyaltyCycle.StartFirst(_patient);
    }

    private LoyaltyAccount Account(int points, int referrals) =>
        LoyaltyAccount.OpenFor(_patient).WithBalances(points, referrals);

    // BUSINESS.md §6 Tier 1: Kompozitni ispun, 500,000 points + 1 referral.
    private static Reward KompozitniIspun() => new(ClinicId, "Kompozitni ispun", 500_000, 1);

    // --- eligibility ----------------------------------------------------------

    [Fact]
    public void NewAccount_StartsWithZeroReferralBalance()
    {
        Assert.Equal(0, LoyaltyAccount.OpenFor(_patient).ReferralBalance);
    }

    // BUSINESS.md §10 examples, plus the exact thresholds.
    [Theory]
    [InlineData(600_000, 1, true)]
    [InlineData(500_000, 1, true)]
    [InlineData(499_999, 1, false)]
    [InlineData(600_000, 0, false)]
    [InlineData(400_000, 2, false)]
    [InlineData(1_000_000, 5, true)]
    public void IsUnlockedBy_RequiresBothPointsAndReferrals(int points, int referrals, bool unlocked)
    {
        Assert.Equal(unlocked, KompozitniIspun().IsUnlockedBy(Account(points, referrals)));
    }

    [Theory]
    [InlineData(1_000_000, 5, RewardStatus.Available)]
    [InlineData(999_999, 5, RewardStatus.Locked)]
    [InlineData(1_000_000, 4, RewardStatus.Locked)]
    public void StatusFor_Tier3_AtAndBelowItsThresholds(int points, int referrals, RewardStatus expected)
    {
        var tier3 = new Reward(ClinicId, "Kućno izbeljivanje zuba", 1_000_000, 5);

        Assert.Equal(expected, tier3.StatusFor(Account(points, referrals), _cycle, redeemedInCycle: false, DateTime.UtcNow));
    }

    [Fact]
    public void IsUnlockedBy_AnAccountOfAnotherClinic_IsFalse()
    {
        var otherClinicReward = new Reward(Guid.NewGuid(), "Kompozitni ispun", 500_000, 1);

        Assert.False(otherClinicReward.IsUnlockedBy(Account(600_000, 1)));
    }

    // BUSINESS.md §5.1: no rewards before registration and linking - no cycle.
    [Fact]
    public void StatusFor_WithoutActiveCycle_IsLocked()
    {
        Assert.Equal(RewardStatus.Locked, KompozitniIspun().StatusFor(Account(600_000, 1), null, false, DateTime.UtcNow));
    }

    [Fact]
    public void StatusFor_InAnExpiredCycle_IsLocked()
    {
        var expired = LoyaltyCycle.StartFirst(_patient).StartedOn(DateTime.UtcNow.AddYears(-1).AddMinutes(-1));

        Assert.Equal(RewardStatus.Locked, KompozitniIspun().StatusFor(Account(600_000, 1), expired, false, DateTime.UtcNow));
    }

    [Fact]
    public void StatusFor_ARewardTheClinicNoLongerOffers_IsLocked()
    {
        var reward = KompozitniIspun();
        reward.Deactivate();

        Assert.Equal(RewardStatus.Locked, reward.StatusFor(Account(600_000, 1), _cycle, false, DateTime.UtcNow));
    }

    [Fact]
    public void StatusFor_RedeemedInTheCycle_IsRedeemed()
    {
        Assert.Equal(RewardStatus.Redeemed, KompozitniIspun().StatusFor(Account(600_000, 1), _cycle, true, DateTime.UtcNow));
    }

    // BUSINESS.md §12: each reward is evaluated independently.
    [Fact]
    public void StatusFor_SeveralRewards_AreEvaluatedIndependently()
    {
        var account = Account(650_000, 3);

        Assert.Equal(RewardStatus.Available, new Reward(ClinicId, "Peskiranje", 300_000, 1).StatusFor(account, _cycle, false, DateTime.UtcNow));
        Assert.Equal(RewardStatus.Available, new Reward(ClinicId, "15% popusta na fiksnu protezu", 600_000, 3).StatusFor(account, _cycle, false, DateTime.UtcNow));
        Assert.Equal(RewardStatus.Locked, new Reward(ClinicId, "30% popusta na hiruršku ekstrakciju umnjaka", 700_000, 3).StatusFor(account, _cycle, false, DateTime.UtcNow));
    }

    // --- redemption -----------------------------------------------------------

    // BUSINESS.md §13.2: balances are unchanged after the redemption.
    [Fact]
    public void RedeemReward_RecordsRedemptionAndZeroChangeTransaction_WithoutChangingBalances()
    {
        var account = Account(750_000, 3);
        var reward = new Reward(ClinicId, "Peskiranje", 300_000, 1);

        var (redemption, transaction) = account.RedeemReward(reward, _cycle, false, AdminUserId);

        Assert.Equal(750_000, account.Points);
        Assert.Equal(3, account.ReferralBalance);
        Assert.Null(account.UpdatedAt);

        Assert.Equal(reward.Id, redemption.RewardId);
        Assert.Equal(_patient.Id, redemption.PatientId);
        Assert.Equal(ClinicId, redemption.ClinicId);
        Assert.Equal(_cycle.Id, redemption.LoyaltyCycleId);
        Assert.Equal(AdminUserId, redemption.RedeemedBy);

        Assert.Equal(LoyaltyTransactionType.RewardRedeemed, transaction.Type);
        Assert.Equal(0, transaction.PointsChange);
        Assert.Equal(0, transaction.ReferralChange);
        Assert.Equal(reward.Id, transaction.RewardId);
        Assert.Equal(AdminUserId, transaction.CreatedBy);
        Assert.Equal(redemption.RedeemedAt, transaction.CreatedAt);
        Assert.Null(transaction.ServiceId);
    }

    // BUSINESS.md §12/§13.2: redeeming one reward never locks another.
    [Fact]
    public void RedeemReward_LeavesOtherUnlockedRewardsAvailable()
    {
        var account = Account(750_000, 3);
        var tier1 = new Reward(ClinicId, "Peskiranje", 300_000, 1);
        var tier2 = new Reward(ClinicId, "15% popusta na fiksnu protezu", 600_000, 3);

        account.RedeemReward(tier1, _cycle, false, AdminUserId);

        Assert.Equal(RewardStatus.Available, tier2.StatusFor(account, _cycle, false, DateTime.UtcNow));
        account.RedeemReward(tier2, _cycle, false, AdminUserId);
    }

    [Theory]
    [InlineData(499_999, 1)]
    [InlineData(500_000, 0)]
    public void RedeemReward_RequirementNotMet_IsRejected(int points, int referrals)
    {
        Assert.Throws<DomainException>(() =>
            Account(points, referrals).RedeemReward(KompozitniIspun(), _cycle, false, AdminUserId));
    }

    [Fact]
    public void RedeemReward_AlreadyRedeemedInTheCycle_IsRejected()
    {
        Assert.Throws<DomainException>(() =>
            Account(600_000, 1).RedeemReward(KompozitniIspun(), _cycle, true, AdminUserId));
    }

    [Fact]
    public void RedeemReward_InactiveReward_IsRejected()
    {
        var reward = KompozitniIspun();
        reward.Deactivate();

        Assert.Throws<DomainException>(() => Account(600_000, 1).RedeemReward(reward, _cycle, false, AdminUserId));
    }

    [Fact]
    public void RedeemReward_InAnExpiredCycle_IsRejected()
    {
        var expired = LoyaltyCycle.StartFirst(_patient).StartedOn(DateTime.UtcNow.AddYears(-2));

        Assert.Throws<DomainException>(() => Account(600_000, 1).RedeemReward(KompozitniIspun(), expired, false, AdminUserId));
    }

    [Fact]
    public void RedeemReward_RewardOfAnotherClinic_IsRejected()
    {
        var otherClinicReward = new Reward(Guid.NewGuid(), "Kompozitni ispun", 500_000, 1);

        Assert.Throws<DomainException>(() => Account(600_000, 1).RedeemReward(otherClinicReward, _cycle, false, AdminUserId));
    }

    [Fact]
    public void RedeemReward_WithAnotherPatientsCycle_IsRejected()
    {
        var other = new Patient(ClinicId, "Marko", "Jovanovic", "+381600000001");
        other.LinkUser(Guid.NewGuid());

        Assert.Throws<DomainException>(() =>
            Account(600_000, 1).RedeemReward(KompozitniIspun(), LoyaltyCycle.StartFirst(other), false, AdminUserId));
    }

    [Fact]
    public void RedeemReward_WithoutConfirmingUser_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Account(600_000, 1).RedeemReward(KompozitniIspun(), _cycle, false, Guid.Empty));
    }
}
