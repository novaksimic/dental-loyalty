using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

public sealed class LoyaltyCycleConfiguration : IEntityTypeConfiguration<LoyaltyCycle>
{
    public void Configure(EntityTypeBuilder<LoyaltyCycle> builder)
    {
        builder.ToTable("LoyaltyCycles");

        builder.HasKey(lc => lc.Id);

        builder.Property(lc => lc.Status)
            .IsRequired();

        builder.Property(lc => lc.StartedAt)
            .IsRequired();

        // Null exactly while the cycle is active (BUSINESS.md §5.2-§5.4).
        builder.Property(lc => lc.EndedAt);

        // Restrict, like the rest of the loyalty history: cycles are historical
        // records and must never disappear through a cascade (BUSINESS.md §5.4).
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(lc => lc.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(lc => lc.PatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // DATA.md §14: only one cycle can be active for a Patient at a time; any
        // number of ended cycles may remain as history.
        builder.HasIndex(lc => lc.PatientId)
            .IsUnique()
            .HasFilter($"[Status] = {(int)LoyaltyCycleStatus.Active}");

        builder.HasIndex(lc => lc.ClinicId);
    }
}
