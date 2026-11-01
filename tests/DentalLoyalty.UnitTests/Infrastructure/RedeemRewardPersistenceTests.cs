using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// Reward eligibility and redemption (BUSINESS.md §9-§13, §19, §20) through
// GetPatientRewardsHandler, RedeemRewardHandler and the real repositories on a
// real relational engine (in-memory SQLite) built from the production
// configurations. Every repository shares one context per operation, as in an
// API request.
//
// SQLite has no rowversion, so this context emulates it (a database default plus
// an update trigger), as RecordPatientServicePersistenceTests does; SQL Server
// behaviour is proven by SqlServerRewardRedemptionTests.
public sealed class RedeemRewardPersistenceTests : IDisposable
{
    private const string RowVersionTrigger = "TR_LoyaltyAccounts_RowVersion";

    private readonly SqliteConnection _connection;

    public RedeemRewardPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw(
            $"""
            CREATE TRIGGER {RowVersionTrigger} AFTER UPDATE ON LoyaltyAccounts
            BEGIN
                UPDATE LoyaltyAccounts SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
            END;
            """);
    }

    public void Dispose() => _connection.Dispose();

    private SqliteRowVersionContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new SqliteRowVersionContext(options);
    }

    // The shared clinic, admin and two linked patients with active cycles, and
    // the BUSINESS.md §6-§8 reward catalog for the clinic.
    private async Task<(LoyaltyAccountTestData Data, Dictionary<string, Reward> Rewards)> SeedAsync()
    {
        await using var context = CreateContext();
        var data = await LoyaltyAccountTestData.SeedAsync(context);

        var rewards = new[]
        {
            new Reward(data.Clinic.Id, "Peskiranje", 300_000, 1),
            new Reward(data.Clinic.Id, "Uklanjanje kamenca + poliranje", 400_000, 1),
            new Reward(data.Clinic.Id, "Kompozitni ispun", 500_000, 1),
            new Reward(data.Clinic.Id, "15% popusta na fiksnu protezu", 600_000, 3),
            new Reward(data.Clinic.Id, "30% popusta na hiruršku ekstrakciju umnjaka", 700_000, 3),
            new Reward(data.Clinic.Id, "Kućno izbeljivanje zuba", 1_000_000, 5),
        };

        context.Rewards.AddRange(rewards);
        await context.SaveChangesAsync();

        return (data, rewards.ToDictionary(r => r.Name));
    }

    private async Task SetBalancesAsync(Guid patientId, int points, int referrals)
    {
        await using var context = CreateContext();
        var account = await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == patientId);
        account.WithBalances(points, referrals);
        await context.SaveChangesAsync();
    }

    private static RedeemRewardHandler CreateHandler(DentalLoyaltyDbContext context, Guid? clinicId, Guid? userId)
    {
        var accounts = new LoyaltyAccountRepository(context);
        return new(
            new PatientRepository(context),
            new RewardRepository(context),
            accounts,
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            clinicId is null ? FakeCurrentTenant.NoTenant() : FakeCurrentTenant.ForClinic(clinicId.Value),
            userId is null ? FakeCurrentUser.NoUser() : FakeCurrentUser.For(userId.Value));
    }

    // One API request: a fresh context, the admin of data's clinic as caller.
    private async Task<RedeemRewardResult> RedeemAsync(
        LoyaltyAccountTestData data,
        Guid patientId,
        Guid rewardId,
        Guid? clinicId = null)
    {
        await using var context = CreateContext();
        return await CreateHandler(context, clinicId ?? data.Clinic.Id, data.Admin.Id)
            .HandleAsync(new RedeemRewardCommand(patientId, rewardId));
    }

    private async Task<PatientRewardsResult?> GetRewardsAsync(Guid clinicId, Guid patientId)
    {
        await using var context = CreateContext();
        var accounts = new LoyaltyAccountRepository(context);
        return await new GetPatientRewardsHandler(
                new PatientRepository(context),
                new RewardRepository(context),
                accounts,
                LoyaltyCycleProgressionTestFactory.Create(context, accounts),
                FakeCurrentTenant.ForClinic(clinicId))
            .HandleAsync(new GetPatientRewardsQuery(patientId));
    }

    private async Task<(int Points, int Referrals, int Transactions, int Redemptions)> ReadStateAsync(Guid patientId)
    {
        await using var context = CreateContext();
        var account = await context.LoyaltyAccounts.AsNoTracking().SingleAsync(a => a.PatientId == patientId);
        var transactions = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId);
        var redemptions = await context.RewardRedemptions.CountAsync(rr => rr.PatientId == patientId);
        return (account.Points, account.ReferralBalance, transactions, redemptions);
    }

    private void FailInsertsInto(string table)
    {
        // table is always a table name written in this test class, never input.
        var sql = $"""
            CREATE TRIGGER TR_Fail_{table} BEFORE INSERT ON {table}
            BEGIN
                SELECT RAISE(ABORT, 'forced failure');
            END;
            """;

        using var context = CreateContext();
        context.Database.ExecuteSqlRaw(sql);
    }

    // --- eligibility ----------------------------------------------------------

    [Fact]
    public async Task GetRewards_EvaluatesEachRewardIndependently_AgainstCurrentBalances()
    {
        var (data, _) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 650_000, 3);

        var result = await GetRewardsAsync(data.Clinic.Id, data.PatientA.Id);

        Assert.NotNull(result);
        Assert.Equal(650_000, result.Points);
        Assert.Equal(3, result.ReferralBalance);
        Assert.Equal(
            new[]
            {
                ("Peskiranje", RewardStatus.Available),
                ("Uklanjanje kamenca + poliranje", RewardStatus.Available),
                ("Kompozitni ispun", RewardStatus.Available),
                ("15% popusta na fiksnu protezu", RewardStatus.Available),
                ("30% popusta na hiruršku ekstrakciju umnjaka", RewardStatus.Locked),
                ("Kućno izbeljivanje zuba", RewardStatus.Locked),
            },
            result.Rewards.Select(r => (r.Name, r.Status)));
    }

    // Points alone never unlock a reward (BUSINESS.md §10) - the case for every
    // patient until referral rewards are implemented.
    [Fact]
    public async Task GetRewards_WithPointsButNoReferrals_AreAllLocked()
    {
        var (data, _) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, LoyaltyAccount.MaximumPointsBalance, 0);

        var result = await GetRewardsAsync(data.Clinic.Id, data.PatientA.Id);

        Assert.All(result!.Rewards, r => Assert.Equal(RewardStatus.Locked, r.Status));
    }

    [Fact]
    public async Task GetRewards_ShowsRedeemedRewards_IncludingOnesTheClinicNoLongerOffers()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 650_000, 3);
        await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id);

        await using (var context = CreateContext())
        {
            (await context.Rewards.SingleAsync(r => r.Id == rewards["Peskiranje"].Id)).Deactivate();
            (await context.Rewards.SingleAsync(r => r.Id == rewards["Kompozitni ispun"].Id)).Deactivate();
            await context.SaveChangesAsync();
        }

        var result = await GetRewardsAsync(data.Clinic.Id, data.PatientA.Id);

        Assert.Equal(RewardStatus.Redeemed, Assert.Single(result!.Rewards, r => r.Name == "Peskiranje").Status);
        Assert.DoesNotContain(result.Rewards, r => r.Name == "Kompozitni ispun");
    }

    [Fact]
    public async Task GetRewards_ForPatientOfAnotherClinic_IsNotFound()
    {
        var (data, _) = await SeedAsync();

        Assert.Null(await GetRewardsAsync(Guid.NewGuid(), data.PatientA.Id));
    }

    // --- successful redemption ------------------------------------------------

    // BUSINESS.md §13.2: 750,000 points and 3 referrals before and after.
    [Fact]
    public async Task EligibleReward_IsRedeemed_WithoutDeductingPointsOrReferrals()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 750_000, 3);

        var result = await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id);

        Assert.Equal(RedeemRewardStatus.Redeemed, result.Status);
        Assert.Equal((750_000, 3, 1, 1), await ReadStateAsync(data.PatientA.Id));

        await using var verify = CreateContext();
        var redemption = await verify.RewardRedemptions.SingleAsync();
        Assert.Equal(result.RedemptionId, redemption.Id);
        Assert.Equal(data.CycleA.Id, redemption.LoyaltyCycleId);
        Assert.Equal(data.Admin.Id, redemption.RedeemedBy);

        var transaction = await verify.LoyaltyTransactions.SingleAsync();
        Assert.Equal(LoyaltyTransactionType.RewardRedeemed, transaction.Type);
        Assert.Equal(0, transaction.PointsChange);
        Assert.Equal(0, transaction.ReferralChange);
        Assert.Equal(rewards["Peskiranje"].Id, transaction.RewardId);
        Assert.Equal(data.Admin.Id, transaction.CreatedBy);
    }

    // BUSINESS.md §12: several available rewards are each redeemable.
    [Fact]
    public async Task SeveralAvailableRewards_CanEachBeRedeemed()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 650_000, 3);

        foreach (var name in new[] { "15% popusta na fiksnu protezu", "Peskiranje", "Kompozitni ispun" })
        {
            Assert.Equal(RedeemRewardStatus.Redeemed, (await RedeemAsync(data, data.PatientA.Id, rewards[name].Id)).Status);
        }

        Assert.Equal((650_000, 3, 3, 3), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task ExactlyAtTheThreshold_IsRedeemed()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);

        Assert.Equal(RedeemRewardStatus.Redeemed, (await RedeemAsync(data, data.PatientA.Id, rewards["Kućno izbeljivanje zuba"].Id)).Status);
    }

    // --- rejected redemptions -------------------------------------------------

    [Theory]
    [InlineData(499_999, 1)]
    [InlineData(500_000, 0)]
    [InlineData(400_000, 2)]
    public async Task RequirementNotMet_IsNotEligible_AndNothingIsRecorded(int points, int referrals)
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, points, referrals);

        var result = await RedeemAsync(data, data.PatientA.Id, rewards["Kompozitni ispun"].Id);

        Assert.Equal(RedeemRewardStatus.NotEligible, result.Status);
        Assert.Equal((points, referrals, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task RepeatedRedemptionInTheSameCycle_IsRejected_AndRecordedOnlyOnce()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id);
        var repeated = await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id);

        Assert.Equal(RedeemRewardStatus.AlreadyRedeemed, repeated.Status);
        Assert.Equal((600_000, 1, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // The redemption belongs to the patient's own cycle: another patient can
    // redeem the same reward.
    [Fact]
    public async Task SameRewardForAnotherPatient_IsRedeemedSeparately()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);
        await SetBalancesAsync(data.PatientB.Id, 600_000, 1);

        Assert.Equal(RedeemRewardStatus.Redeemed, (await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id)).Status);
        Assert.Equal(RedeemRewardStatus.Redeemed, (await RedeemAsync(data, data.PatientB.Id, rewards["Peskiranje"].Id)).Status);
    }

    // BUSINESS.md §5.1: no rewards before registration and linking.
    [Fact]
    public async Task UnlinkedPatient_IsNotEligible_AndNothingIsRecorded()
    {
        var (data, rewards) = await SeedAsync();
        Patient patient;

        await using (var context = CreateContext())
        {
            patient = new Patient(data.Clinic.Id, "Pera", "Peric", "+381600000009");
            context.Patients.Add(patient);
            context.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient).WithBalances(600_000, 1));
            await context.SaveChangesAsync();
        }

        var result = await RedeemAsync(data, patient.Id, rewards["Peskiranje"].Id);

        Assert.Equal(RedeemRewardStatus.NotEligible, result.Status);
        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(patient.Id));
        Assert.All((await GetRewardsAsync(data.Clinic.Id, patient.Id))!.Rewards, r => Assert.Equal(RewardStatus.Locked, r.Status));
    }

    // BUSINESS.md §5.3, §5.4: the expired cycle is ended first, so the reward is
    // evaluated in the next cycle, against its reset balances.
    [Fact]
    public async Task ExpiredCycle_RollsOver_AndTheRewardIsNoLongerUnlocked()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        await using (var context = CreateContext())
        {
            (await context.LoyaltyCycles.SingleAsync(c => c.Id == data.CycleA.Id)).StartedOn(DateTime.UtcNow.AddYears(-1).AddMinutes(-1));
            await context.SaveChangesAsync();
        }

        var result = await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id);

        Assert.Equal(RedeemRewardStatus.NotEligible, result.Status);
        // Reset to zero by the CycleReset transaction; nothing redeemed.
        Assert.Equal((0, 0, 1, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task InactivePatient_IsRejected_AndNothingIsRecorded()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        await using (var context = CreateContext())
        {
            (await context.Patients.SingleAsync(p => p.Id == data.PatientA.Id)).Deactivate();
            await context.SaveChangesAsync();
        }

        Assert.Equal(RedeemRewardStatus.PatientInactive, (await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id)).Status);
        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task RewardTheClinicNoLongerOffers_IsUnavailable_AndNothingIsRecorded()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        await using (var context = CreateContext())
        {
            (await context.Rewards.SingleAsync(r => r.Id == rewards["Peskiranje"].Id)).Deactivate();
            await context.SaveChangesAsync();
        }

        Assert.Equal(RedeemRewardStatus.RewardUnavailable, (await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id)).Status);
        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    // --- tenant isolation -----------------------------------------------------

    [Fact]
    public async Task CallerFromAnotherClinic_CannotRedeemForThisClinicsPatient()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        var result = await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id, clinicId: Guid.NewGuid());

        Assert.Equal(RedeemRewardStatus.PatientNotFound, result.Status);
        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task RewardOfAnotherClinic_IsNotFound_AndNothingIsRecorded()
    {
        var (data, _) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);
        Reward otherReward;

        await using (var context = CreateContext())
        {
            var otherClinic = new Clinic("Clinic B");
            otherReward = new Reward(otherClinic.Id, "Peskiranje", 0, 0);
            context.Clinics.Add(otherClinic);
            context.Rewards.Add(otherReward);
            await context.SaveChangesAsync();
        }

        Assert.Equal(RedeemRewardStatus.RewardNotFound, (await RedeemAsync(data, data.PatientA.Id, otherReward.Id)).Status);
        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task WithoutTenantOrUser_FailsClosed_AndNothingIsRecorded()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        await using (var context = CreateContext())
        {
            Assert.Equal(RedeemRewardStatus.PatientNotFound, (await CreateHandler(context, null, data.Admin.Id)
                .HandleAsync(new RedeemRewardCommand(data.PatientA.Id, rewards["Peskiranje"].Id))).Status);
            Assert.Equal(RedeemRewardStatus.PatientNotFound, (await CreateHandler(context, data.Clinic.Id, null)
                .HandleAsync(new RedeemRewardCommand(data.PatientA.Id, rewards["Peskiranje"].Id))).Status);
        }

        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    // --- concurrency and atomicity --------------------------------------------

    // Two requests for the same reward that both read the account before either
    // saved: the second fails and persists nothing, so it is redeemed once.
    [Fact]
    public async Task ConcurrentRedemptionsOfTheSameReward_AreRecordedOnce()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);
        var reward = rewards["Peskiranje"];

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);
        var firstAccount = (await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var secondAccount = (await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;

        var (firstRedemption, firstTransaction) = firstAccount.RedeemReward(reward, data.CycleA, false, data.Admin.Id);
        var (secondRedemption, secondTransaction) = secondAccount.RedeemReward(reward, data.CycleA, false, data.Admin.Id);

        await firstRepository.SaveRewardRedemptionAsync(firstRedemption, firstAccount, firstTransaction);

        var secondAttempt = await Record.ExceptionAsync(() =>
            secondRepository.SaveRewardRedemptionAsync(secondRedemption, secondAccount, secondTransaction));

        // The stale account fails the rowversion check before the unique key is
        // reached; either way, nothing from the second attempt is saved.
        Assert.True(secondAttempt is ConcurrencyConflictException or DuplicateRewardRedemptionException);
        Assert.Equal((600_000, 1, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // Even with a current account, the (cycle, reward) unique key stops a second
    // redemption that got past the handler's check.
    [Fact]
    public async Task SecondRedemptionInTheSameCycle_IsRejectedByTheDatabase()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);
        var reward = rewards["Peskiranje"];
        await RedeemAsync(data, data.PatientA.Id, reward.Id);

        await using var context = CreateContext();
        var repository = new LoyaltyAccountRepository(context);
        var account = (await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var (redemption, transaction) = account.RedeemReward(reward, data.CycleA, alreadyRedeemedInCycle: false, data.Admin.Id);

        await Assert.ThrowsAsync<DuplicateRewardRedemptionException>(() =>
            repository.SaveRewardRedemptionAsync(redemption, account, transaction));

        Assert.Equal((600_000, 1, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // A redemption decided on balances that changed before it was saved fails:
    // its eligibility must be evaluated against the current state (BACKEND & API
    // ARCHITECTURE.md §26).
    [Fact]
    public async Task RedemptionDecidedOnAStaleAccount_Conflicts_AndPersistsNothing()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        await using var context = CreateContext();
        var repository = new LoyaltyAccountRepository(context);
        var account = (await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var (redemption, transaction) = account.RedeemReward(rewards["Peskiranje"], data.CycleA, false, data.Admin.Id);

        // Another operation changes the account after it was read.
        await SetBalancesAsync(data.PatientA.Id, 610_000, 1);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            repository.SaveRewardRedemptionAsync(redemption, account, transaction));

        Assert.Equal((610_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    // And an operation that read the account before a redemption was saved fails
    // in turn, because the redemption advanced the account's version.
    [Fact]
    public async Task ServicePointsDecidedBeforeARedemption_Conflict()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);

        await using var context = CreateContext();
        var repository = new LoyaltyAccountRepository(context);
        var account = (await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var patientService = data.RecordService(data.PatientA);
        var earning = account.EarnServicePoints(patientService, data.CycleA);

        await RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            repository.SaveServicePointsAsync(patientService, account, earning));

        Assert.Equal((600_000, 1, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task WhenTheTransactionInsertFails_NoRedemptionIsRecorded()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);
        FailInsertsInto("LoyaltyTransactions");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id));

        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task WhenTheRedemptionInsertFails_NoTransactionIsRecorded()
    {
        var (data, rewards) = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 1);
        FailInsertsInto("RewardRedemptions");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            RedeemAsync(data, data.PatientA.Id, rewards["Peskiranje"].Id));

        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    private sealed class SqliteRowVersionContext : DentalLoyaltyDbContext
    {
        public SqliteRowVersionContext(DbContextOptions<DentalLoyaltyDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LoyaltyAccount>(account =>
            {
                account.ToTable(table => table.HasTrigger(RowVersionTrigger));
                account.Property(a => a.RowVersion).HasDefaultValueSql("randomblob(8)");
            });
        }
    }
}
