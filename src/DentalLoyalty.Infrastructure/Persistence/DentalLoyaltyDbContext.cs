using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence;

public class DentalLoyaltyDbContext : DbContext
{
    public DentalLoyaltyDbContext(
        DbContextOptions<DentalLoyaltyDbContext> options)
        : base(options)
    {
    }

    public DbSet<Clinic> Clinics => Set<Clinic>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<PatientService> PatientServices => Set<PatientService>();

    public DbSet<LoyaltyAccount> LoyaltyAccounts => Set<LoyaltyAccount>();

    public DbSet<LoyaltyConfiguration> LoyaltyConfigurations => Set<LoyaltyConfiguration>();

    public DbSet<LoyaltyTransaction> LoyaltyTransactions => Set<LoyaltyTransaction>();

    public DbSet<LoyaltyCycle> LoyaltyCycles => Set<LoyaltyCycle>();

    public DbSet<Reward> Rewards => Set<Reward>();

    public DbSet<Referral> Referrals => Set<Referral>();

    public DbSet<RewardRedemption> RewardRedemptions => Set<RewardRedemption>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(DentalLoyaltyDbContext).Assembly);
    }
}