using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }

    public Guid? ClinicId { get; private set; }

    public string Email { get; private set; }

    public string PasswordHash { get; private set; }

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    private User()
    {
        Email = null!;
        PasswordHash = null!;
    }

    public User(
        Guid? clinicId,
        string email,
        string passwordHash,
        UserRole role)
    {
        ValidateClinicAssociation(role, clinicId);

        Id = Guid.NewGuid();
        ClinicId = clinicId;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
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

    private static void ValidateClinicAssociation(
        UserRole role,
        Guid? clinicId)
    {
        if (role == UserRole.PlatformAdmin && clinicId is not null)
        {
            throw new ArgumentException(
                "PlatformAdmin must not be associated with a Clinic.",
                nameof(clinicId));
        }

        if (role == UserRole.ClinicAdmin && clinicId is null)
        {
            throw new ArgumentException(
                "ClinicAdmin must be associated with a Clinic.",
                nameof(clinicId));
        }

        if (role == UserRole.Patient && clinicId is null)
        {
            throw new ArgumentException(
                "Patient must be associated with a Clinic.",
                nameof(clinicId));
        }
    }
}
