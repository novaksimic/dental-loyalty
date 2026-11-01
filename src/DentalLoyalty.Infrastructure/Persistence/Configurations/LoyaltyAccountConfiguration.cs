using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

public sealed class LoyaltyAccountConfiguration : IEntityTypeConfiguration<LoyaltyAccount>
{
    public void Configure(EntityTypeBuilder<LoyaltyAccount> builder)
    {
        builder.ToTable("LoyaltyAccounts");

        builder.HasKey(la => la.Id);

        builder.Property(la => la.Points)
            .IsRequired();

        builder.Property(la => la.ReferralBalance)
            .IsRequired();

        builder.Property(la => la.CreatedAt)
            .IsRequired();

        // ADR-005: SQL Server rowversion as the optimistic concurrency token. EF
        // Core adds it to the WHERE clause of every UPDATE of the account, so an
        // update based on a stale read affects no row and fails.
        builder.Property(la => la.RowVersion)
            .IsRowVersion();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(la => la.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(la => la.PatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // A Patient belongs to exactly one Clinic (DATA.md §6), so a unique
        // constraint on PatientId alone enforces "one LoyaltyAccount per
        // Patient per Clinic" without needing a composite key.
        builder.HasIndex(la => la.PatientId)
            .IsUnique();
    }
}
