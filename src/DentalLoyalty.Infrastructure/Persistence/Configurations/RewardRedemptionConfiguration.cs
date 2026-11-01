using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

public sealed class RewardRedemptionConfiguration : IEntityTypeConfiguration<RewardRedemption>
{
    public void Configure(EntityTypeBuilder<RewardRedemption> builder)
    {
        builder.ToTable("RewardRedemptions");

        builder.HasKey(rr => rr.Id);

        builder.Property(rr => rr.RedeemedAt)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(rr => rr.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(rr => rr.PatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Reward>()
            .WithMany()
            .HasForeignKey(rr => rr.RewardId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Restrict, like the rest of the loyalty history: redemptions are kept
        // when a cycle ends (BUSINESS.md §5.4).
        builder.HasOne<LoyaltyCycle>()
            .WithMany()
            .HasForeignKey(rr => rr.LoyaltyCycleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // The Clinic Admin who confirmed the redemption (BUSINESS.md §13.1).
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rr => rr.RedeemedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // A reward is redeemed at most once per cycle (BUSINESS.md §5.2, §9;
        // DATA.md §13 "prevention of duplicate redemption"), so a repeated or
        // concurrent redemption cannot be recorded twice.
        builder.HasIndex(rr => new { rr.LoyaltyCycleId, rr.RewardId })
            .IsUnique();

        // Patient's redemption history in chronological order.
        builder.HasIndex(rr => new { rr.PatientId, rr.RedeemedAt });

        // Clinic-wide redemption activity feed (admin/analytics).
        builder.HasIndex(rr => new { rr.ClinicId, rr.RedeemedAt });

        // DATA.md §25 names PatientId+RewardId as a potential composite index
        // (supports checking a patient's redemption(s) of a specific reward).
        builder.HasIndex(rr => new { rr.PatientId, rr.RewardId });
    }
}
