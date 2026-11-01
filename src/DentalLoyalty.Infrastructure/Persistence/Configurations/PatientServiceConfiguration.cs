using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

public sealed class PatientServiceConfiguration : IEntityTypeConfiguration<PatientService>
{
    public void Configure(EntityTypeBuilder<PatientService> builder)
    {
        builder.ToTable("PatientServices");

        builder.HasKey(ps => ps.Id);

        builder.Property(ps => ps.PriceAtTimeOfService)
            .HasPrecision(18, 2);

        builder.Property(ps => ps.EarnedPoints)
            .IsRequired();

        builder.Property(ps => ps.UsedAt)
            .IsRequired();

        builder.Property(ps => ps.RecordedAt)
            .IsRequired();

        // The user who recorded the service. Optional only for services recorded
        // before it was stored.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ps => ps.RecordedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(ps => ps.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(ps => ps.PatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(ps => ps.ServiceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Patient's service-usage history in chronological order (loyalty history).
        builder.HasIndex(ps => new { ps.PatientId, ps.UsedAt });

        // Clinic-wide service usage activity feed.
        builder.HasIndex(ps => new { ps.ClinicId, ps.UsedAt });
    }
}
