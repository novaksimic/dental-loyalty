namespace DentalLoyalty.Domain.Entities;

public class Service
{
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
}