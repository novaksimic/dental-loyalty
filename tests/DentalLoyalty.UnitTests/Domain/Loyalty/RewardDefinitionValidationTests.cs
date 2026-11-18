using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

// The lengths a reward definition's name and description can have - the
// Rewards.Name / Rewards.Description columns - checked by the Reward itself so
// an over-long value is a validation error, not a database failure. The
// requirement rules are covered by RewardTests.
public class RewardDefinitionValidationTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();

    [Fact]
    public void NameAndDescription_AtMaximumLength_AreAccepted()
    {
        var reward = new Reward(
            ClinicId,
            new string('n', Reward.MaximumNameLength),
            300_000,
            1,
            new string('d', Reward.MaximumDescriptionLength));

        Assert.Equal(Reward.MaximumNameLength, reward.Name.Length);
        Assert.Equal(Reward.MaximumDescriptionLength, reward.Description!.Length);
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Reward(ClinicId, new string('n', Reward.MaximumNameLength + 1), 300_000, 1));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Create_DescriptionTooLong_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new Reward(ClinicId, "Peskiranje", 300_000, 1, new string('d', Reward.MaximumDescriptionLength + 1)));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void Update_DescriptionTooLong_Throws_AndChangesNothing()
    {
        var reward = new Reward(ClinicId, "Peskiranje", 300_000, 1, "Opis");

        Assert.Throws<ArgumentException>(() =>
            reward.Update("Novo ime", 400_000, 2, new string('d', Reward.MaximumDescriptionLength + 1)));

        Assert.Equal("Peskiranje", reward.Name);
        Assert.Equal(300_000, reward.RequiredPoints);
        Assert.Equal(1, reward.RequiredReferrals);
        Assert.Equal("Opis", reward.Description);
        Assert.Null(reward.UpdatedAt);
    }

    // ADR-008 decision 2 contemplates rewards requiring more referrals than a
    // cycle can earn; such a definition is valid (it is just never unlocked).
    [Fact]
    public void RequiredReferrals_AboveTheCycleMaximum_IsAccepted()
    {
        var reward = new Reward(ClinicId, "Poseban poklon", 0, LoyaltyAccount.MaximumReferralRewards + 1);

        Assert.Equal(LoyaltyAccount.MaximumReferralRewards + 1, reward.RequiredReferrals);
    }
}
