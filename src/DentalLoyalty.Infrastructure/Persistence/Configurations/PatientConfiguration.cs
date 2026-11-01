using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.PhoneNumber)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(p => p.Email)
            .HasMaxLength(256);

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Optional 1:0..1 link to the Patient's authenticated User account
        // (DATA.md §4/§6). Restrict avoids cascade-deleting a Patient's
        // historical loyalty data if the linked User is ever removed.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // One User is linked to at most one Patient (the /api/patients/me lookup
        // by UserId relies on this). Filtered so the many Patients without a
        // User account (UserId NULL) remain allowed.
        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL");

        // Supports tenant-scoped patient listings ordered/filtered by creation date
        // (Clinic Admin "Patient Management" screen, PROJECT PILLAR.md §11).
        builder.HasIndex(p => new { p.ClinicId, p.CreatedAt });
    }
}
