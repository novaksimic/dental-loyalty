using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;
using DentalLoyalty.Domain.Loyalty;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

// Cycle completion (BUSINESS.md §5.2), expiry (§5.3) and the start of the next
// cycle with the reset of active progress (§5.4; ADR-008) on the domain model:
// LoyaltyCycle.IsCompletedBy and LoyaltyCycleLifecycle.
public class LoyaltyCycleLifecycleTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();

    private readonly Patient _patient;
    private readonly LoyaltyCycle _cycle;

    public LoyaltyCycleLifecycleTests()
    {
        _patient = new Patient(ClinicId, "Ana", "Petrovic", "+381600000000");
        _patient.LinkUser(Guid.NewGuid());
        _cycle = LoyaltyCycle.StartFirst(_patient).StartedOn(DateTime.UtcNow.AddMonths(-3));
    }

    private LoyaltyAccount Account(int points, int referrals) =>
        LoyaltyAccount.OpenFor(_patient).WithBalances(points, referrals);

    private static Reward Reward(int points, int referrals, bool active = true)
    {
        var reward = new Reward(ClinicId, $"Reward {points}/{referrals}", points, referrals);

        if (!active)
        {
            reward.Deactivate();
        }

        return reward;
    }

    private static readonly IReadOnlySet<Guid> NoneRedeemed = new HashSet<Guid>();

    // --- completion conditions (§5.2) -----------------------------------------

    [Fact]
    public void IsCompletedBy_AllThreeConditionsMet_IsTrue()
    {
        var rewards = new[] { Reward(300_000, 1), Reward(1_000_000, 5) };
        var redeemed = rewards.Select(r => r.Id).ToHashSet();

        Assert.True(_cycle.IsCompletedBy(Account(1_000_000, 5), rewards, redeemed, DateTime.UtcNow));
    }

    [Fact]
    public void IsCompletedBy_PointsReached_ReferralsMissing_IsFalse()
    {
        Assert.False(_cycle.IsCompletedBy(Account(1_000_000, 4), [], NoneRedeemed, DateTime.UtcNow));
    }

    [Fact]
    public void IsCompletedBy_ReferralsReached_PointsMissing_IsFalse()
    {
        Assert.False(_cycle.IsCompletedBy(Account(999_999, 5), [], NoneRedeemed, DateTime.UtcNow));
    }

    [Fact]
    public void IsCompletedBy_BothThresholdsReached_ARewardStillAvailable_IsFalse()
    {
        var rewards = new[] { Reward(300_000, 1), Reward(1_000_000, 5) };
        var redeemed = new HashSet<Guid> { rewards[0].Id };

        Assert.False(_cycle.IsCompletedBy(Account(1_000_000, 5), rewards, redeemed, DateTime.UtcNow));
    }

    // ADR-008: a reward the patient can no longer redeem does not block it.
    [Fact]
    public void IsCompletedBy_AnUnlockedRewardTheClinicWithdrew_DoesNotBlock()
    {
        var rewards = new[] { Reward(300_000, 1), Reward(400_000, 1, active: false) };
        var redeemed = new HashSet<Guid> { rewards[0].Id };

        Assert.True(_cycle.IsCompletedBy(Account(1_000_000, 5), rewards, redeemed, DateTime.UtcNow));
    }

    // A reward requiring more referrals than can be earned is never unlocked.
    [Fact]
    public void IsCompletedBy_ARewardNeverUnlocked_DoesNotBlock()
    {
        var rewards = new[] { Reward(0, LoyaltyAccount.MaximumReferralRewards + 1) };

        Assert.True(_cycle.IsCompletedBy(Account(1_000_000, 5), rewards, NoneRedeemed, DateTime.UtcNow));
    }

    [Fact]
    public void IsCompletedBy_AfterTheCycleExpired_IsFalse()
    {
        Assert.False(_cycle.IsCompletedBy(Account(1_000_000, 5), [], NoneRedeemed, _cycle.ExpiresAt));
    }

    // --- completion -----------------------------------------------------------

    [Fact]
    public void CompleteIfSatisfied_EndsTheCycle_StartsTheNextAtThatMoment_AndResetsProgress()
    {
        var account = Account(1_000_000, 5);
        var now = DateTime.UtcNow;

        var transition = LoyaltyCycleLifecycle.CompleteIfSatisfied(account, _cycle, [], NoneRedeemed, now);

        Assert.NotNull(transition);
        Assert.Same(_cycle, transition.Ended);
        Assert.Equal(LoyaltyCycleStatus.Completed, _cycle.Status);
        Assert.Equal(now, _cycle.EndedAt);

        Assert.Equal(LoyaltyCycleStatus.Active, transition.Next.Status);
        Assert.Equal(now, transition.Next.StartedAt);
        Assert.Equal(now.AddYears(1), transition.Next.ExpiresAt);
        Assert.Equal((_patient.Id, ClinicId), (transition.Next.PatientId, transition.Next.ClinicId));

        Assert.Equal((0, 0), (account.Points, account.ReferralBalance));
        Assert.Equal(LoyaltyTransactionType.CycleReset, transition.Reset.Type);
        Assert.Equal((-1_000_000, -5), (transition.Reset.PointsChange, transition.Reset.ReferralChange));
        Assert.Equal(_cycle.Id, transition.Reset.LoyaltyCycleId);
        Assert.Equal(now, transition.Reset.CreatedAt);
        Assert.Null(transition.Reset.CreatedBy);
    }

    [Fact]
    public void CompleteIfSatisfied_WhenAConditionIsMissing_ChangesNothing()
    {
        var account = Account(1_000_000, 4);

        Assert.Null(LoyaltyCycleLifecycle.CompleteIfSatisfied(account, _cycle, [], NoneRedeemed, DateTime.UtcNow));

        Assert.Equal(LoyaltyCycleStatus.Active, _cycle.Status);
        Assert.Null(_cycle.EndedAt);
        Assert.Equal((1_000_000, 4), (account.Points, account.ReferralBalance));
    }

    // A cycle ends exactly once.
    [Fact]
    public void ACompletedCycle_CannotCompleteOrExpireAgain()
    {
        var account = Account(1_000_000, 5);
        LoyaltyCycleLifecycle.CompleteIfSatisfied(account, _cycle, [], NoneRedeemed, DateTime.UtcNow);

        Assert.Null(LoyaltyCycleLifecycle.CompleteIfSatisfied(account.WithBalances(1_000_000, 5), _cycle, [], NoneRedeemed, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => LoyaltyCycleLifecycle.RollOverExpired(account, _cycle, _cycle.ExpiresAt.AddDays(1)));
    }

    // --- expiry (§5.3) --------------------------------------------------------

    [Fact]
    public void RollOverExpired_OneTickBeforeTheOneYearEnd_DoesNothing()
    {
        var account = Account(400_000, 2);

        Assert.Empty(LoyaltyCycleLifecycle.RollOverExpired(account, _cycle, _cycle.ExpiresAt.AddTicks(-1)));

        Assert.Equal(LoyaltyCycleStatus.Active, _cycle.Status);
        Assert.Equal((400_000, 2), (account.Points, account.ReferralBalance));
    }

    // Exactly one year after the start the cycle has expired; it is not a
    // completion, even with progress, and the next starts at the expiry.
    [Fact]
    public void RollOverExpired_AtTheOneYearEnd_ExpiresTheCycle_AndStartsTheNextAtItsEnd()
    {
        var account = Account(400_000, 2);
        var expiresAt = _cycle.ExpiresAt;

        var transition = Assert.Single(LoyaltyCycleLifecycle.RollOverExpired(account, _cycle, expiresAt));

        Assert.Equal(LoyaltyCycleStatus.Expired, _cycle.Status);
        Assert.Equal(expiresAt, _cycle.EndedAt);
        Assert.Equal(expiresAt, transition.Next.StartedAt);
        Assert.Equal(LoyaltyCycleStatus.Active, transition.Next.Status);
        Assert.Equal((0, 0), (account.Points, account.ReferralBalance));
        Assert.Equal((-400_000, -2), (transition.Reset.PointsChange, transition.Reset.ReferralChange));
        Assert.Equal(expiresAt, transition.Reset.CreatedAt);
    }

    // The end is the one-year boundary, not the moment the expiry is processed.
    [Fact]
    public void RollOverExpired_ProcessedLater_StillEndsAtTheOneYearBoundary()
    {
        var account = Account(0, 0);
        var expiresAt = _cycle.ExpiresAt;

        var transition = Assert.Single(LoyaltyCycleLifecycle.RollOverExpired(account, _cycle, expiresAt.AddDays(40)));

        Assert.Equal(expiresAt, _cycle.EndedAt);
        Assert.Equal(expiresAt, transition.Next.StartedAt);
    }

    // A patient inactive for more than a year: every cycle in between expires in
    // turn, each starting at the previous one's end.
    [Fact]
    public void RollOverExpired_AfterSeveralYears_EndsEveryCycleInBetween()
    {
        var account = Account(250_000, 1);
        var start = _cycle.StartedAt;

        var transitions = LoyaltyCycleLifecycle.RollOverExpired(account, _cycle, start.AddYears(3).AddDays(1));

        Assert.Equal(3, transitions.Count);
        Assert.Equal(
            new[] { start.AddYears(1), start.AddYears(2), start.AddYears(3) },
            transitions.Select(t => t.Ended.EndedAt!.Value));
        Assert.All(transitions, t => Assert.Equal(LoyaltyCycleStatus.Expired, t.Ended.Status));
        Assert.All(transitions.Skip(1).Zip(transitions), pair => Assert.Same(pair.Second.Next, pair.First.Ended));
        Assert.Equal(LoyaltyCycleStatus.Active, transitions[^1].Next.Status);
        Assert.Equal(start.AddYears(3), transitions[^1].Next.StartedAt);

        // Only the first reset carries progress; the later ones record zero.
        Assert.Equal(new[] { -250_000, 0, 0 }, transitions.Select(t => t.Reset.PointsChange));
    }

    // Late activity (ADR-008): the next cycle accepts only what happens in it.
    [Fact]
    public void NextCycle_DoesNotAcceptAReferralExaminedInTheExpiredCycle()
    {
        var expiresAt = _cycle.ExpiresAt;
        var next = Assert.Single(LoyaltyCycleLifecycle.RollOverExpired(Account(0, 0), _cycle, expiresAt.AddDays(1))).Next;

        Assert.False(next.WasRunningAt(expiresAt.AddDays(-1)));
        Assert.True(next.WasRunningAt(expiresAt));
    }
}
