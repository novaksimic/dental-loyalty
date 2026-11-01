using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

public sealed class LoyaltyConfigurationConfiguration : IEntityTypeConfiguration<LoyaltyConfiguration>
{
    public void Configure(EntityTypeBuilder<LoyaltyConfiguration> builder)
    {
        builder.ToTable("LoyaltyConfigurations", table =>
        {
            // Mirrors LoyaltyConfiguration's domain validation, so the database
            // itself never holds a ratio points could not be calculated from.
            table.HasCheckConstraint(
                "CK_LoyaltyConfigurations_SpendAmountRsd_Positive",
                "[SpendAmountRsd] > 0");

            table.HasCheckConstraint(
                "CK_LoyaltyConfigurations_PointsAwarded_Positive",
                "[PointsAwarded] > 0");
        });

        builder.HasKey(lc => lc.Id);

        builder.Property(lc => lc.SpendAmountRsd)
            .HasPrecision(18, LoyaltyConfiguration.SpendAmountDecimalPlaces)
            .IsRequired();

        builder.Property(lc => lc.PointsAwarded)
            .IsRequired();

        builder.Property(lc => lc.CreatedAt)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(lc => lc.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // ADR-003 / DATA.md §8: Clinic 1 ─── 1 LoyaltyConfiguration.
        builder.HasIndex(lc => lc.ClinicId)
            .IsUnique();
    }
}
