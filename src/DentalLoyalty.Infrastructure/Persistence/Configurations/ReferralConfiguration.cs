using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

public sealed class ReferralConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> builder)
    {
        builder.ToTable("Referrals");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.RewardsForfeited)
            .IsRequired();

        // Two validations of the same referral cannot both be saved.
        builder.Property(r => r.RowVersion)
            .IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.ValidatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(r => r.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Two distinct relationships to Patient (referrer / referred).
        // Neither Referral nor Patient exposes a CLR navigation collection,
        // so each is configured independently via its own foreign key.
        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(r => r.ReferrerPatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(r => r.ReferredPatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // A patient is referred - enters the loyalty system as a new patient
        // (BUSINESS.md §4.1) - at most once. A cancelled referral does not count.
        builder.HasIndex(r => r.ReferredPatientId)
            .IsUnique()
            .HasFilter($"[Status] <> {(int)ReferralStatus.Cancelled}");

        // Tenant-scoped referral activity over time.
        builder.HasIndex(r => new { r.ClinicId, r.CreatedAt });
    }
}
