using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DentalLoyalty.Infrastructure.Persistence.Configurations;

// Append-only loyalty history (BUSINESS.md §14; ADR-004). All relationships are
// Restrict: history must never disappear through a cascade (DATA.md §24).
public sealed class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        builder.ToTable("LoyaltyTransactions");

        builder.HasKey(lt => lt.Id);

        builder.Property(lt => lt.Type)
            .IsRequired();

        builder.Property(lt => lt.PointsChange)
            .IsRequired();

        builder.Property(lt => lt.ReferralChange)
            .IsRequired();

        builder.Property(lt => lt.CreatedAt)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(lt => lt.ClinicId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(lt => lt.PatientId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // Optional references (BUSINESS.md §14: "when applicable").
        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(lt => lt.ServiceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // The service occurrence an earning transaction credits. At most one
        // transaction per occurrence, so a recorded service can never earn twice;
        // different occurrences of the same catalog service each can.
        builder.HasOne<PatientService>()
            .WithMany()
            .HasForeignKey(lt => lt.PatientServiceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(lt => lt.PatientServiceId)
            .IsUnique()
            .HasFilter("[PatientServiceId] IS NOT NULL");

        builder.HasOne<Reward>()
            .WithMany()
            .HasForeignKey(lt => lt.RewardId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<Referral>()
            .WithMany()
            .HasForeignKey(lt => lt.ReferralId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // A referral rewards each of its patients at most once (BUSINESS.md §4.2).
        builder.HasIndex(lt => new { lt.ReferralId, lt.PatientId })
            .IsUnique()
            .HasFilter("[ReferralId] IS NOT NULL");

        // The cycle whose end a CycleReset records (BUSINESS.md §5.4). At most one
        // per cycle, so a cycle can never be ended - and its progress reset - twice.
        builder.HasOne<LoyaltyCycle>()
            .WithMany()
            .HasForeignKey(lt => lt.LoyaltyCycleId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(lt => lt.LoyaltyCycleId)
            .IsUnique()
            .HasFilter("[LoyaltyCycleId] IS NOT NULL");

        // The user responsible for the operation, where applicable.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(lt => lt.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Patient's loyalty history in chronological order.
        builder.HasIndex(lt => new { lt.PatientId, lt.CreatedAt });

        // Clinic-wide loyalty activity (tenant-scoped admin views).
        builder.HasIndex(lt => new { lt.ClinicId, lt.CreatedAt });
    }
}
