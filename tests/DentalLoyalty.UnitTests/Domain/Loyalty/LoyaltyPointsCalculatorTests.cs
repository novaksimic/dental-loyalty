using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Loyalty;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

public class LoyaltyPointsCalculatorTests
{
    // The platform default scoring configuration: 100 RSD = 1,000 points (BUSINESS.md §3.1).
    private static readonly LoyaltyConfiguration Default = LoyaltyConfiguration.CreateDefault(Guid.NewGuid());

    private static long DefaultPoints(string amountRsd) =>
        LoyaltyPointsCalculator.CalculatePoints(decimal.Parse(amountRsd), Default);

    private static long Points(string amountRsd, decimal spendAmountRsd, int pointsAwarded) =>
        LoyaltyPointsCalculator.CalculatePoints(
            decimal.Parse(amountRsd),
            new LoyaltyConfiguration(Guid.NewGuid(), spendAmountRsd, pointsAwarded));

    // BUSINESS.md §3.1 table.
    [Theory]
    [InlineData("100", 1_000)]
    [InlineData("500", 5_000)]
    [InlineData("1000", 10_000)]
    [InlineData("10000", 100_000)]
    [InlineData("100000", 1_000_000)]
    public void CalculatePoints_WithDefaultConfiguration_MatchesBusinessScoringTable(string amountRsd, long expectedPoints)
    {
        Assert.Equal(expectedPoints, DefaultPoints(amountRsd));
    }

    // BUSINESS.md §16 calculator example: 7,500 RSD -> 75,000 points.
    [Fact]
    public void CalculatePoints_WithDefaultConfiguration_MatchesBusinessCalculatorExample()
    {
        Assert.Equal(75_000, DefaultPoints("7500"));
    }

    [Fact]
    public void CalculatePoints_ZeroAmount_GeneratesZeroPoints()
    {
        Assert.Equal(0, DefaultPoints("0"));
    }

    // The calculation itself is not capped - the 1,000,000 maximum applies to
    // the balance (LoyaltyAccount), not to what an amount generates.
    [Fact]
    public void CalculatePoints_AmountAboveMaximumProgress_IsNotCapped()
    {
        Assert.Equal(1_500_000, DefaultPoints("150000"));
    }

    [Theory]
    [InlineData("7500.50", 75_005)]
    [InlineData("0.1", 1)]
    public void CalculatePoints_DecimalAmountYieldingWholePoints_IsExact(string amountRsd, long expectedPoints)
    {
        Assert.Equal(expectedPoints, DefaultPoints(amountRsd));
    }

    // BUSINESS.md §3.2 table: midpoint rounding away from zero.
    [Theory]
    [InlineData("1234.54", 12_345)]
    [InlineData("1234.55", 12_346)]
    [InlineData("1234.56", 12_346)]
    [InlineData("7500.00", 75_000)]
    public void CalculatePoints_MatchesBusinessRoundingExamples(string amountRsd, long expectedPoints)
    {
        Assert.Equal(expectedPoints, DefaultPoints(amountRsd));
    }

    // A fractional part below 0.5 rounds down, 0.5 or greater rounds up.
    [Theory]
    [InlineData("0.01", 0)]
    [InlineData("0.04", 0)]
    [InlineData("0.05", 1)]
    [InlineData("99.99", 1_000)]
    public void CalculatePoints_FractionalPoints_AreRoundedMidpointAwayFromZero(string amountRsd, long expectedPoints)
    {
        Assert.Equal(expectedPoints, DefaultPoints(amountRsd));
    }

    // BUSINESS.md §3.1/§3.2: points = amount / spend amount × points awarded,
    // with the clinic's own ratio, then rounded.
    [Theory]
    [InlineData("1234.56", "1", 1, 1_235)]        // 1 RSD = 1 point: 1,234.56 -> 1,235
    [InlineData("250", "100", 5, 13)]             // 12.5 -> 13 (midpoint rounds up)
    [InlineData("249", "100", 5, 12)]             // 12.45 -> 12
    [InlineData("1.50", "3", 1, 1)]               // exactly 0.5 -> 1, despite a non-terminating 1/3 ratio
    [InlineData("1.49", "3", 1, 0)]               // 0.4966... -> 0
    [InlineData("7500", "50", 2_000, 300_000)]
    [InlineData("10", "0.01", 1, 1_000)]
    public void CalculatePoints_WithClinicConfiguration_UsesThatClinicsRatio(
        string amountRsd,
        string spendAmountRsd,
        int pointsAwarded,
        long expectedPoints)
    {
        Assert.Equal(expectedPoints, Points(amountRsd, decimal.Parse(spendAmountRsd), pointsAwarded));
    }

    [Fact]
    public void CalculatePoints_SameAmount_DiffersBetweenClinicConfigurations()
    {
        var clinicA = LoyaltyConfiguration.CreateDefault(Guid.NewGuid());
        var clinicB = new LoyaltyConfiguration(Guid.NewGuid(), 100m, 500);

        Assert.Equal(75_000, LoyaltyPointsCalculator.CalculatePoints(7_500m, clinicA));
        Assert.Equal(37_500, LoyaltyPointsCalculator.CalculatePoints(7_500m, clinicB));
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("-100")]
    public void CalculatePoints_NegativeAmount_IsRejected(string amountRsd)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DefaultPoints(amountRsd));
    }

    [Fact]
    public void CalculatePoints_AmountTooLargeForPoints_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LoyaltyPointsCalculator.CalculatePoints(decimal.MaxValue, Default));
    }

    [Fact]
    public void CalculatePoints_ResultAboveLongRange_IsRejected()
    {
        var generous = new LoyaltyConfiguration(Guid.NewGuid(), 0.01m, int.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LoyaltyPointsCalculator.CalculatePoints(10_000_000_000m, generous));
    }

    [Fact]
    public void CalculatePoints_WithoutConfiguration_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            LoyaltyPointsCalculator.CalculatePoints(100m, null!));
    }
}
