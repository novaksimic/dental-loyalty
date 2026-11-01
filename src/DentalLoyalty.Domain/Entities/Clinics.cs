namespace DentalLoyalty.Domain.Entities;

public class Clinic
{
    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private Clinic()
    {
        Name = null!;
    }

    public Clinic(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string name)
    {
        Name = name;
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