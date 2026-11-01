using DentalLoyalty.Domain.Entities;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

public class LoyaltyConfigurationTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();

    // BUSINESS.md §3.1: platform default, 100 RSD = 1,000 points.
    [Fact]
    public void CreateDefault_Is100RsdFor1000Points()
    {
        var configuration = LoyaltyConfiguration.CreateDefault(ClinicId);

        Assert.Equal(ClinicId, configuration.ClinicId);
        Assert.Equal(100m, configuration.SpendAmountRsd);
        Assert.Equal(1_000, configuration.PointsAwarded);
        Assert.NotEqual(Guid.Empty, configuration.Id);
        Assert.Null(configuration.UpdatedAt);
    }

    [Theory]
    [InlineData("100", 1_000)]
    [InlineData("1", 1)]
    [InlineData("0.01", 1)]
    [InlineData("250.50", 3_000)]
    [InlineData("9999999999999999.99", 2147483647)]
    public void NewConfiguration_WithValidRatio_IsCreated(string spendAmountRsd, int pointsAwarded)
    {
        var configuration = new LoyaltyConfiguration(ClinicId, decimal.Parse(spendAmountRsd), pointsAwarded);

        Assert.Equal(decimal.Parse(spendAmountRsd), configuration.SpendAmountRsd);
        Assert.Equal(pointsAwarded, configuration.PointsAwarded);
    }

    [Fact]
    public void NewConfiguration_WithoutClinic_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new LoyaltyConfiguration(Guid.Empty, 100m, 1_000));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    [InlineData("0.001")]          // more precision than the stored decimal(18,2)
    [InlineData("100.005")]
    [InlineData("10000000000000000")] // above the decimal(18,2) range
    public void NewConfiguration_WithInvalidSpendAmount_IsRejected(string spendAmountRsd)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LoyaltyConfiguration(ClinicId, decimal.Parse(spendAmountRsd), 1_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NewConfiguration_WithNonPositivePointsAwarded_IsRejected(int pointsAwarded)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LoyaltyConfiguration(ClinicId, 100m, pointsAwarded));
    }

    [Fact]
    public void UpdateScoring_WithValidRatio_ChangesRatio()
    {
        var configuration = LoyaltyConfiguration.CreateDefault(ClinicId);

        configuration.UpdateScoring(50m, 2_000);

        Assert.Equal(50m, configuration.SpendAmountRsd);
        Assert.Equal(2_000, configuration.PointsAwarded);
        Assert.NotNull(configuration.UpdatedAt);
        Assert.Equal(ClinicId, configuration.ClinicId);
    }

    [Theory]
    [InlineData("0", 1_000)]
    [InlineData("100.001", 1_000)]
    [InlineData("100", 0)]
    [InlineData("100", -5)]
    public void UpdateScoring_WithInvalidRatio_IsRejectedAndConfigurationUnchanged(string spendAmountRsd, int pointsAwarded)
    {
        var configuration = LoyaltyConfiguration.CreateDefault(ClinicId);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            configuration.UpdateScoring(decimal.Parse(spendAmountRsd), pointsAwarded));

        Assert.Equal(100m, configuration.SpendAmountRsd);
        Assert.Equal(1_000, configuration.PointsAwarded);
        Assert.Null(configuration.UpdatedAt);
    }
}
