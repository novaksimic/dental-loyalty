using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Domain.Loyalty;

// The single place the service scoring rule is applied (BUSINESS.md §3.1, §16, §22):
// points = amount / spend amount × points awarded, using the scoring configuration
// of the clinic the calculation is for (with the platform default, 100 RSD = 1,000
// points). The caller supplies that clinic's LoyaltyConfiguration.
//
// Calculated in decimal, never floating point (BACKEND & API ARCHITECTURE.md §10).
// The amount is multiplied before dividing so the result is exact wherever it is
// representable, and the result is then rounded to a whole number of points using
// midpoint rounding away from zero (BUSINESS.md §3.2): a fractional part of 0.5 or
// greater rounds up, below 0.5 rounds down (e.g. with the default, 1,234.56 RSD ->
// 12,345.6 -> 12,346). Only the points are rounded, never the amount.
//
// The result is the points an amount generates, before the 1,000,000 maximum
// balance is applied - that cap belongs to LoyaltyAccount, not to the calculation.
public static class LoyaltyPointsCalculator
{
    public static long CalculatePoints(
        decimal amountRsd,
        LoyaltyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (amountRsd < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountRsd),
                amountRsd,
                "Amount cannot be negative.");
        }

        decimal points;

        try
        {
            points = amountRsd * configuration.PointsAwarded / configuration.SpendAmountRsd;
        }
        catch (OverflowException)
        {
            throw AmountTooLarge(amountRsd);
        }

        var roundedPoints = Math.Round(points, MidpointRounding.AwayFromZero);

        if (roundedPoints > long.MaxValue)
        {
            throw AmountTooLarge(amountRsd);
        }

        return (long)roundedPoints;
    }

    private static ArgumentOutOfRangeException AmountTooLarge(decimal amountRsd) =>
        new(
            nameof(amountRsd),
            amountRsd,
            "Amount is too large to convert to points.");
}
