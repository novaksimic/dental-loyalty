using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// The loyalty cycle lifecycle against SQL Server itself, on a disposable
// database built from the real migrations: ending a cycle and starting the next
// under the real filtered unique index (one active cycle per patient), the
// rowversion returned with the update, the one-reset-per-cycle index, and a
// failed insert rolling back the whole transition. Skipped unless
// DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerLoyaltyCycleLifecycleTests
{
    private static async Task<List<LoyaltyCycle>> ReadCyclesAsync(SqlServerTestDatabase database, Guid patientId)
    {
        await using var context = database.CreateContext();
        return await context.LoyaltyCycles.AsNoTracking()
            .Where(c => c.PatientId == patientId)
            .OrderBy(c => c.StartedAt)
            .ToListAsync();
    }

    private static async Task<(int Points, int Referrals, int Resets)> ReadStateAsync(SqlServerTestDatabase database, Guid patientId)
    {
        await using var context = database.CreateContext();
        var account = await context.LoyaltyAccounts.AsNoTracking().SingleAsync(a => a.PatientId == patientId);
        var resets = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId && t.Type == LoyaltyTransactionType.CycleReset);
        return (account.Points, account.ReferralBalance, resets);
    }

    private static RedeemRewardHandler CreateRedeemHandler(DentalLoyaltyDbContext context, LoyaltyAccountTestData data)
    {
        var accounts = new LoyaltyAccountRepository(context);
        return new(
            new PatientRepository(context),
            new RewardRepository(context),
            accounts,
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            FakeCurrentTenant.ForClinic(data.Clinic.Id),
            FakeCurrentUser.For(data.Admin.Id));
    }

    private static RecordPatientServiceHandler CreateServiceHandler(DentalLoyaltyDbContext context, LoyaltyAccountTestData data)
    {
        var accounts = new LoyaltyAccountRepository(context);
        return new(
            new PatientRepository(context),
            new ServiceRepository(context),
            new LoyaltyConfigurationRepository(context),
            accounts,
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            new PatientServiceRepository(context),
            FakeCurrentTenant.ForClinic(data.Clinic.Id),
            FakeCurrentUser.For(data.Admin.Id));
    }

    // The shared test data, patient A at both targets, and the Tier 3 reward.
    private static async Task<(LoyaltyAccountTestData Data, Reward Reward)> SeedAtTargetsAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var data = await LoyaltyAccountTestData.SeedAsync(context);
        var reward = new Reward(data.Clinic.Id, "Kućno izbeljivanje zuba", 1_000_000, 5);
        context.Rewards.Add(reward);
        (await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id)).WithBalances(1_000_000, 5);
        await context.SaveChangesAsync();
        return (data, reward);
    }

    [SqlServerIntegrationFact]
    public async Task RedeemingTheLastReward_CompletesTheCycle_UnderTheOneActiveCycleIndex()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, reward) = await SeedAtTargetsAsync(database);

        RedeemRewardResult result;
        await using (var context = database.CreateContext())
        {
            result = await CreateRedeemHandler(context, data).HandleAsync(new RedeemRewardCommand(data.PatientA.Id, reward.Id));
        }

        Assert.True(result.CycleCompleted);
        var cycles = await ReadCyclesAsync(database, data.PatientA.Id);
        Assert.Equal(new[] { LoyaltyCycleStatus.Completed, LoyaltyCycleStatus.Active }, cycles.Select(c => c.Status));
        Assert.Equal(cycles[0].EndedAt, cycles[1].StartedAt);
        Assert.Equal((0, 0, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    // The rollover is saved first and the service then saves the account again
    // in the same unit of work, against the rowversion SQL Server returned.
    [SqlServerIntegrationFact]
    public async Task ExpiredCycle_RollsOver_AndTheServiceEarnsInTheNextCycle()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        LoyaltyAccountTestData data;
        var start = DateTime.UtcNow.AddYears(-1).AddHours(-1);
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
            (await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id)).WithBalances(600_000, 2);
            (await context.LoyaltyCycles.SingleAsync(c => c.Id == data.CycleA.Id)).StartedOn(start);
            await context.SaveChangesAsync();
        }

        RecordPatientServiceResult result;
        await using (var context = database.CreateContext())
        {
            result = await CreateServiceHandler(context, data)
                .HandleAsync(new RecordPatientServiceCommand(data.PatientA.Id, Guid.NewGuid(), data.Service.Id, null));
        }

        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, result.Status);
        var cycles = await ReadCyclesAsync(database, data.PatientA.Id);
        Assert.Equal(new[] { LoyaltyCycleStatus.Expired, LoyaltyCycleStatus.Active }, cycles.Select(c => c.Status));
        Assert.Equal(cycles[0].EndedAt, cycles[1].StartedAt);
        Assert.Equal((75_000, 0, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentCompletions_SecondConflicts_AndTheCycleEndsOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
            (await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id)).WithBalances(1_000_000, 5);
            await context.SaveChangesAsync();
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var (firstAccounts, firstAccount) = await PrepareCompletionAsync(first, data);
        var (secondAccounts, secondAccount) = await PrepareCompletionAsync(second, data);

        await firstAccounts.SaveBalanceChangesAsync([firstAccount], []);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondAccounts.SaveBalanceChangesAsync([secondAccount], []));

        Assert.Equal(
            new[] { LoyaltyCycleStatus.Completed, LoyaltyCycleStatus.Active },
            (await ReadCyclesAsync(database, data.PatientA.Id)).Select(c => c.Status));
        Assert.Equal((0, 0, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    [SqlServerIntegrationFact]
    public async Task WhenStartingTheNextCycleFails_TheCompletingRedemptionIsRolledBack()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, reward) = await SeedAtTargetsAsync(database);

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [TR_Fail_LoyaltyCycles] ON [LoyaltyCycles] AFTER INSERT AS
                THROW 50000, 'forced failure', 1;
                """);
        }

        await using (var context = database.CreateContext())
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                CreateRedeemHandler(context, data).HandleAsync(new RedeemRewardCommand(data.PatientA.Id, reward.Id)));
        }

        Assert.Equal(LoyaltyCycleStatus.Active, Assert.Single(await ReadCyclesAsync(database, data.PatientA.Id)).Status);
        Assert.Equal((1_000_000, 5, 0), await ReadStateAsync(database, data.PatientA.Id));
        await using var readContext = database.CreateContext();
        Assert.False(await readContext.RewardRedemptions.AnyAsync(r => r.PatientId == data.PatientA.Id));
    }

    private static async Task<(LoyaltyAccountRepository Accounts, LoyaltyAccount Account)> PrepareCompletionAsync(
        DentalLoyaltyDbContext context,
        LoyaltyAccountTestData data)
    {
        var accounts = new LoyaltyAccountRepository(context);
        var progression = LoyaltyCycleProgressionTestFactory.Create(context, accounts);
        var account = (await accounts.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var cycle = (await progression.GetCurrentCycleAsync(account, DateTime.UtcNow, save: false))!;
        Assert.NotNull(await progression.CompleteIfSatisfiedAsync(account, cycle, DateTime.UtcNow));
        return (accounts, account);
    }
}
