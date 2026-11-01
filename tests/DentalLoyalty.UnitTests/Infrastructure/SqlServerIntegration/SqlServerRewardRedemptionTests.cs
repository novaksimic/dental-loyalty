using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// Reward redemption against SQL Server itself, on a disposable database built
// from the real migrations: the real rowversion, the unique index on
// RewardRedemptions (LoyaltyCycleId, RewardId), SQL Server's duplicate-key errors
// and a failed insert rolling back the whole redemption. Skipped unless
// DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerRewardRedemptionTests
{
    private static async Task<(int Points, int Referrals, int Transactions, int Redemptions)> ReadStateAsync(SqlServerTestDatabase database, Guid patientId)
    {
        await using var context = database.CreateContext();
        var account = await context.LoyaltyAccounts.AsNoTracking().SingleAsync(a => a.PatientId == patientId);
        var transactions = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId);
        var redemptions = await context.RewardRedemptions.CountAsync(rr => rr.PatientId == patientId);
        return (account.Points, account.ReferralBalance, transactions, redemptions);
    }

    private static RedeemRewardHandler CreateHandler(DentalLoyaltyDbContext context, LoyaltyAccountTestData data)
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

    // The shared test data, patient A at 600,000 points and 1 referral credit, and
    // the Tier 1 300,000-point reward.
    private static async Task<(LoyaltyAccountTestData Data, Reward Reward)> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var data = await LoyaltyAccountTestData.SeedAsync(context);
        var reward = new Reward(data.Clinic.Id, "Peskiranje", 300_000, 1);
        context.Rewards.Add(reward);
        (await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id)).WithBalances(600_000, 1);
        await context.SaveChangesAsync();
        return (data, reward);
    }

    [SqlServerIntegrationFact]
    public async Task Handler_RedeemsWithoutDeducting_AndRepeatedRedemptionIsRejected()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, reward) = await SeedAsync(database);

        RedeemRewardResult first;
        await using (var context = database.CreateContext())
        {
            first = await CreateHandler(context, data).HandleAsync(new RedeemRewardCommand(data.PatientA.Id, reward.Id));
        }

        RedeemRewardResult repeated;
        await using (var context = database.CreateContext())
        {
            repeated = await CreateHandler(context, data).HandleAsync(new RedeemRewardCommand(data.PatientA.Id, reward.Id));
        }

        Assert.Equal(RedeemRewardStatus.Redeemed, first.Status);
        Assert.Equal(RedeemRewardStatus.AlreadyRedeemed, repeated.Status);
        Assert.Equal((600_000, 1, 1, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    [SqlServerIntegrationFact]
    public async Task SecondRedemptionInTheSameCycle_IsRejectedByTheUniqueIndex()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, reward) = await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await CreateHandler(context, data).HandleAsync(new RedeemRewardCommand(data.PatientA.Id, reward.Id));
        }

        await using (var context = database.CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = (await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
            var (redemption, transaction) = account.RedeemReward(reward, data.CycleA, alreadyRedeemedInCycle: false, data.Admin.Id);

            var failure = await Assert.ThrowsAsync<DuplicateRewardRedemptionException>(() =>
                repository.SaveRewardRedemptionAsync(redemption, account, transaction));
            Assert.Contains("IX_RewardRedemptions_LoyaltyCycleId_RewardId", failure.InnerException!.InnerException!.Message);
        }

        Assert.Equal((600_000, 1, 1, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentRedemptionsOfTheSameReward_SecondConflicts_AndPersistsNothing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, reward) = await SeedAsync(database);

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);
        var firstAccount = (await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var secondAccount = (await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var (firstRedemption, firstTransaction) = firstAccount.RedeemReward(reward, data.CycleA, false, data.Admin.Id);
        var (secondRedemption, secondTransaction) = secondAccount.RedeemReward(reward, data.CycleA, false, data.Admin.Id);

        await firstRepository.SaveRewardRedemptionAsync(firstRedemption, firstAccount, firstTransaction);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondRepository.SaveRewardRedemptionAsync(secondRedemption, secondAccount, secondTransaction));

        Assert.Equal((600_000, 1, 1, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    [SqlServerIntegrationFact]
    public async Task WhenTheTransactionInsertFails_TheRedemptionIsRolledBack()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, reward) = await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER [TR_Fail_LoyaltyTransactions] ON [LoyaltyTransactions] AFTER INSERT AS
                THROW 50000, 'forced failure', 1;
                """);
        }

        await using (var context = database.CreateContext())
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                CreateHandler(context, data).HandleAsync(new RedeemRewardCommand(data.PatientA.Id, reward.Id)));
        }

        Assert.Equal((600_000, 1, 0, 0), await ReadStateAsync(database, data.PatientA.Id));
    }
}
