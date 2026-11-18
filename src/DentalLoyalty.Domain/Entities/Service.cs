namespace DentalLoyalty.Domain.Entities;

// A dental service of a clinic's catalog (BUSINESS.md §15): its name and price
// are clinic-configurable (§22). A recorded service keeps the price it had at
// the time (PatientService.PriceAtTimeOfService), so changing the price never
// changes history.
public class Service
{
    // Services.Name column length (ServiceConfiguration).
    public const int MaximumNameLength = 200;

    // Price is stored as decimal(18,2) (ServiceConfiguration), so values the
    // column could not hold exactly are rejected rather than silently rounded
    // by the database.
    public const int PriceDecimalPlaces = 2;

    public const decimal MaximumPrice = 9_999_999_999_999_999.99m;

    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private Service()
    {
        Name = null!;
    }

    public Service(
        Guid clinicId,
        string name,
        decimal price)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("ClinicId is required.", nameof(clinicId));
        }

        Validate(name, price);

        Id = Guid.NewGuid();
        ClinicId = clinicId;
        Name = name;
        Price = price;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(
        string name,
        decimal price)
    {
        Validate(name, price);

        Name = name;
        Price = price;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    // A price of 0 is allowed (for example a free check-up); it earns no points.
    private static void Validate(
        string name,
        decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Service name is required.", nameof(name));
        }

        if (name.Length > MaximumNameLength)
        {
            throw new ArgumentException(
                $"Service name can be at most {MaximumNameLength} characters.",
                nameof(name));
        }

        if (price < 0 || price > MaximumPrice)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                $"Price must be between 0 and {MaximumPrice} RSD.");
        }

        if (decimal.Round(price, PriceDecimalPlaces) != price)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                $"Price can have at most {PriceDecimalPlaces} decimal places.");
        }
    }
}
