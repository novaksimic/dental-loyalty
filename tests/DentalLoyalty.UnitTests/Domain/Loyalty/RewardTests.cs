using DentalLoyalty.Domain.Entities;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

public class RewardTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();

    // BUSINESS.md §6-§8 reward definitions are all valid.
    [Theory]
    [InlineData("Peskiranje", 300_000, 1)]
    [InlineData("Uklanjanje kamenca + poliranje", 400_000, 1)]
    [InlineData("Kompozitni ispun", 500_000, 1)]
    [InlineData("15% popusta na fiksnu protezu", 600_000, 3)]
    [InlineData("30% popusta na hiruršku ekstrakciju umnjaka", 700_000, 3)]
    [InlineData("Kućno izbeljivanje zuba", 1_000_000, 5)]
    public void NewReward_WithDocumentedRequirements_IsCreated(string name, int requiredPoints, int requiredReferrals)
    {
        var reward = new Reward(ClinicId, name, requiredPoints, requiredReferrals);

        Assert.Equal(name, reward.Name);
        Assert.Equal(requiredPoints, reward.RequiredPoints);
        Assert.Equal(requiredReferrals, reward.RequiredReferrals);
        Assert.True(reward.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NewReward_WithoutName_IsRejected(string? name)
    {
        Assert.Throws<ArgumentException>(() => new Reward(ClinicId, name!, 300_000, 1));
    }

    [Fact]
    public void NewReward_WithoutClinic_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Reward(Guid.Empty, "Peskiranje", 300_000, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void NewReward_WithRequiredPointsOutsideBalanceRange_IsRejected(int requiredPoints)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Reward(ClinicId, "Peskiranje", requiredPoints, 1));
    }

    [Fact]
    public void NewReward_WithNegativeRequiredReferrals_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Reward(ClinicId, "Peskiranje", 300_000, -1));
    }

    [Fact]
    public void Update_WithInvalidValues_IsRejectedAndRewardUnchanged()
    {
        var reward = new Reward(ClinicId, "Peskiranje", 300_000, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => reward.Update("Peskiranje", 1_000_001, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => reward.Update("Peskiranje", 300_000, -1, null));
        Assert.Throws<ArgumentException>(() => reward.Update(" ", 300_000, 1, null));

        Assert.Equal("Peskiranje", reward.Name);
        Assert.Equal(300_000, reward.RequiredPoints);
        Assert.Equal(1, reward.RequiredReferrals);
    }

    [Fact]
    public void Update_WithValidValues_ChangesRequirements()
    {
        var reward = new Reward(ClinicId, "Peskiranje", 300_000, 1);

        reward.Update("Peskiranje", 350_000, 2, "Opis");

        Assert.Equal(350_000, reward.RequiredPoints);
        Assert.Equal(2, reward.RequiredReferrals);
        Assert.Equal("Opis", reward.Description);
        Assert.NotNull(reward.UpdatedAt);
    }
}
