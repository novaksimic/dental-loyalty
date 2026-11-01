namespace DentalLoyalty.Domain.Entities;

// A clinic's scoring configuration (BUSINESS.md §3.1; DATA.md §8; ADR-003):
// PointsAwarded loyalty points are earned for every SpendAmountRsd RSD of service
// amount. Exactly one per Clinic, never shared with or applied to another Clinic.
public class LoyaltyConfiguration
{
    // BUSINESS.md §3.1: platform default for every new clinic, 100 RSD = 1,000 points.
    public const decimal DefaultSpendAmountRsd = 100m;

    public const int DefaultPointsAwarded = 1_000;

    // SpendAmountRsd is stored as decimal(18,2) (LoyaltyConfigurationConfiguration),
    // so values the column could not hold exactly are rejected rather than
    // silently rounded by the database.
    public const int SpendAmountDecimalPlaces = 2;

    public const decimal MaximumSpendAmountRsd = 9_999_999_999_999_999.99m;

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public decimal SpendAmountRsd { get; private set; }

    public int PointsAwarded { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private LoyaltyConfiguration()
    {
    }

    public LoyaltyConfiguration(
        Guid clinicId,
        decimal spendAmountRsd,
        int pointsAwarded)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("ClinicId is required.", nameof(clinicId));
        }

        Validate(spendAmountRsd, pointsAwarded);

        Id = Guid.NewGuid();
        ClinicId = clinicId;
        SpendAmountRsd = spendAmountRsd;
        PointsAwarded = pointsAwarded;
        CreatedAt = DateTime.UtcNow;
    }

    public static LoyaltyConfiguration CreateDefault(Guid clinicId) =>
        new(clinicId, DefaultSpendAmountRsd, DefaultPointsAwarded);

    public void UpdateScoring(
        decimal spendAmountRsd,
        int pointsAwarded)
    {
        Validate(spendAmountRsd, pointsAwarded);

        SpendAmountRsd = spendAmountRsd;
        PointsAwarded = pointsAwarded;
        UpdatedAt = DateTime.UtcNow;
    }

    // Both values must be positive for the ratio to be a ratio: points are
    // calculated as amount / SpendAmountRsd × PointsAwarded (BUSINESS.md §3.2).
    private static void Validate(
        decimal spendAmountRsd,
        int pointsAwarded)
    {
        if (spendAmountRsd <= 0 || spendAmountRsd > MaximumSpendAmountRsd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spendAmountRsd),
                spendAmountRsd,
                $"Spend amount must be greater than 0 and at most {MaximumSpendAmountRsd} RSD.");
        }

        if (decimal.Round(spendAmountRsd, SpendAmountDecimalPlaces) != spendAmountRsd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(spendAmountRsd),
                spendAmountRsd,
                $"Spend amount can have at most {SpendAmountDecimalPlaces} decimal places.");
        }

        if (pointsAwarded <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pointsAwarded),
                pointsAwarded,
                "Points awarded must be greater than 0.");
        }
    }
}
