using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.Application.Referrals.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// The loyalty history reads against SQL Server itself, on a disposable database
// built from the real migrations: the correlated subqueries that resolve service,
// reward, referral and cycle references, the CASE tie-break that orders a reset
// after the redemption that caused it, OFFSET/FETCH paging, tenant scoping, and
// that reading - including an expired cycle shown rolled over - writes nothing.
// History is produced by the real write handlers. Skipped unless
// DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerLoyaltyHistoryTests
{
    private static LoyaltyHistoryStateLoader CreateLoader(DentalLoyaltyDbContext context, ICurrentTenant tenant, ICurrentUser user)
    {
        var accounts = new LoyaltyAccountRepository(context);
        return new LoyaltyHistoryStateLoader(
            new PatientRepository(context),
            accounts,
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            user,
            tenant);
    }

    private static async Task<LoyaltyHistoryResult?> ReadHistoryAsync(
        SqlServerTestDatabase database,
        Guid clinicId,
        LoyaltyHistorySubject subject,
        Guid? userId = null)
    {
        await using var context = database.CreateContext();
        var handler = new GetLoyaltyHistoryHandler(
            CreateLoader(context, FakeCurrentTenant.ForClinic(clinicId), userId is Guid u ? FakeCurrentUser.For(u) : FakeCurrentUser.NoUser()),
            new LoyaltyHistoryRepository(context),
            new RewardRepository(context));
        return await handler.HandleAsync(new GetLoyaltyHistoryQuery(subject));
    }

    private static async Task<GetLoyaltyTransactionsResult> ReadTransactionsAsync(
        SqlServerTestDatabase database,
        Guid clinicId,
        Guid patientId,
        int page = 1,
        int pageSize = 20)
    {
        await using var context = database.CreateContext();
        var handler = new GetLoyaltyTransactionsHandler(
            CreateLoader(context, FakeCurrentTenant.ForClinic(clinicId), FakeCurrentUser.NoUser()),
            new LoyaltyHistoryRepository(context));
        return await handler.HandleAsync(new GetLoyaltyTransactionsQuery(LoyaltyHistorySubject.Patient(patientId), page, pageSize));
    }

    private static async Task RecordServiceAsync(SqlServerTestDatabase database, LoyaltyAccountTestData data, Guid patientId)
    {
        await using var context = database.CreateContext();
        var accounts = new LoyaltyAccountRepository(context);
        var result = await new RecordPatientServiceHandler(
            new PatientRepository(context),
            new ServiceRepository(context),
            new LoyaltyConfigurationRepository(context),
            accounts,
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            new PatientServiceRepository(context),
            FakeCurrentTenant.ForClinic(data.Clinic.Id),
            FakeCurrentUser.For(data.Admin.Id))
            .HandleAsync(new RecordPatientServiceCommand(patientId, Guid.NewGuid(), data.Service.Id, null));
        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, result.Status);
    }

    private static async Task ReferAndValidateAsync(SqlServerTestDatabase database, LoyaltyAccountTestData data, Guid referrerId, Guid referredId)
    {
        Guid referralId;
        await using (var context = database.CreateContext())
        {
            var created = await new CreateReferralHandler(
                new PatientRepository(context),
                new ReferralRepository(context),
                FakeCurrentTenant.ForClinic(data.Clinic.Id),
                FakeCurrentUser.For(data.Admin.Id))
                .HandleAsync(new CreateReferralCommand(referrerId, referredId));
            referralId = created.ReferralId!.Value;
        }

        await using (var context = database.CreateContext())
        {
            var accounts = new LoyaltyAccountRepository(context);
            var result = await new ValidateReferralHandler(
                new ReferralRepository(context),
                new PatientRepository(context),
                accounts,
                new LoyaltyCycleRepository(context),
                LoyaltyCycleProgressionTestFactory.Create(context, accounts),
                FakeCurrentTenant.ForClinic(data.Clinic.Id),
                FakeCurrentUser.For(data.Admin.Id))
                .HandleAsync(new ValidateReferralCommand(referralId, null));
            Assert.Equal(ValidateReferralStatus.Validated, result.Status);
        }
    }

    private static async Task<RedeemRewardResult> RedeemAsync(SqlServerTestDatabase database, LoyaltyAccountTestData data, Guid patientId, Guid rewardId)
    {
        await using var context = database.CreateContext();
        var accounts = new LoyaltyAccountRepository(context);
        return await new RedeemRewardHandler(
            new PatientRepository(context),
            new RewardRepository(context),
            accounts,
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            FakeCurrentTenant.ForClinic(data.Clinic.Id),
            FakeCurrentUser.For(data.Admin.Id))
            .HandleAsync(new RedeemRewardCommand(patientId, rewardId));
    }

    private static async Task<Reward> SeedRewardAsync(SqlServerTestDatabase database, Guid clinicId, string name, int points, int referrals)
    {
        await using var context = database.CreateContext();
        var reward = new Reward(clinicId, name, points, referrals);
        context.Rewards.Add(reward);
        await context.SaveChangesAsync();
        return reward;
    }

    private static async Task<string> SnapshotAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var accounts = await context.LoyaltyAccounts.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Points, a.ReferralBalance, a.RowVersion }).ToListAsync();
        var cycles = await context.LoyaltyCycles.AsNoTracking().OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Status, c.StartedAt, c.EndedAt }).ToListAsync();
        var transactions = await context.LoyaltyTransactions.AsNoTracking().OrderBy(t => t.Id)
            .Select(t => new { t.Id, t.Type, t.PointsChange, t.ReferralChange, t.CreatedAt }).ToListAsync();
        var redemptions = await context.RewardRedemptions.AsNoTracking().CountAsync();
        var referrals = await context.Referrals.AsNoTracking().OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Status, r.RowVersion }).ToListAsync();

        return string.Join("|",
            string.Join(",", accounts.Select(a => $"{a.Id}:{a.Points}:{a.ReferralBalance}:{Convert.ToHexString(a.RowVersion)}")),
            string.Join(",", cycles.Select(c => $"{c.Id}:{c.Status}:{c.StartedAt:O}:{c.EndedAt:O}")),
            string.Join(",", transactions.Select(t => $"{t.Id}:{t.Type}:{t.PointsChange}:{t.ReferralChange}:{t.CreatedAt:O}")),
            redemptions,
            string.Join(",", referrals.Select(r => $"{r.Id}:{r.Status}:{Convert.ToHexString(r.RowVersion)}")));
    }

    [SqlServerIntegrationFact]
    public async Task History_ResolvesEveryReference_AndOrdersNewestFirst()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
        }

        var reward = await SeedRewardAsync(database, data.Clinic.Id, "Peskiranje", 100_000, 1);
        await RecordServiceAsync(database, data, data.PatientA.Id);
        await ReferAndValidateAsync(database, data, data.PatientA.Id, data.PatientB.Id);
        Assert.Equal(RedeemRewardStatus.Redeemed, (await RedeemAsync(database, data, data.PatientA.Id, reward.Id)).Status);

        var history = await ReadHistoryAsync(database, data.Clinic.Id, LoyaltyHistorySubject.CurrentPatient, data.PatientA.UserId);
        var transactions = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id)).Transactions!;

        Assert.NotNull(history);
        Assert.Equal((125_000, 1), (history.Status.Points, history.Status.ReferralBalance));
        Assert.Equal(RewardStatus.Redeemed, Assert.Single(history.Status.Rewards).Status);
        var cycle = Assert.Single(history.Cycles);
        Assert.Equal((LoyaltyCycleStatus.Active, 125_000, 1), (cycle.Status, cycle.Points, cycle.Referrals));
        Assert.Equal("Peskiranje", Assert.Single(cycle.Redemptions).RewardName);
        Assert.Equal(DateTimeKind.Utc, cycle.StartedAt.Kind);
        var referral = Assert.Single(history.Referrals);
        Assert.Equal((ReferralStatus.Completed, false, true, (int?)50_000), (referral.Status, referral.RewardsForfeited, referral.Rewarded, referral.CreditedPoints));

        Assert.Equal(3, transactions.TotalCount);
        Assert.Equal(
            new (LoyaltyTransactionType, int, string?, string?, bool?)[]
            {
                (LoyaltyTransactionType.RewardRedeemed, 0, null, "Peskiranje", null),
                (LoyaltyTransactionType.ReferralPointsEarned, 50_000, null, null, true),
                (LoyaltyTransactionType.ServicePointsEarned, 75_000, data.Service.Name, null, null),
            },
            transactions.Items.Select(t => (t.Type, t.PointsChange, t.ServiceName, t.RewardName, t.IsReferrer)));
        Assert.All(transactions.Items, t => Assert.Equal(DateTimeKind.Utc, t.OccurredAt.Kind));

        var referred = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientB.Id)).Transactions!;
        Assert.False(Assert.Single(referred.Items).IsReferrer);
    }

    // The redemption that completes the cycle and its CycleReset share one
    // timestamp; the reset must sort as the later record on every read.
    [SqlServerIntegrationFact]
    public async Task CompletingRedemption_AndItsReset_AreOrderedDeterministically()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
            (await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id)).WithBalances(1_000_000, 5);
            await context.SaveChangesAsync();
        }

        var reward = await SeedRewardAsync(database, data.Clinic.Id, "Kućno izbeljivanje zuba", 1_000_000, 5);
        Assert.True((await RedeemAsync(database, data, data.PatientA.Id, reward.Id)).CycleCompleted);

        for (var i = 0; i < 3; i++)
        {
            var page = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id)).Transactions!;
            Assert.Equal(
                new[] { LoyaltyTransactionType.CycleReset, LoyaltyTransactionType.RewardRedeemed },
                page.Items.Select(t => t.Type));
            Assert.Equal(page.Items[0].OccurredAt, page.Items[1].OccurredAt);
            Assert.Equal(LoyaltyCycleStatus.Completed, page.Items[0].EndedCycleStatus);
        }

        var history = await ReadHistoryAsync(database, data.Clinic.Id, LoyaltyHistorySubject.Patient(data.PatientA.Id));
        Assert.Equal(new[] { LoyaltyCycleStatus.Active, LoyaltyCycleStatus.Completed }, history!.Cycles.Select(c => c.Status));
        Assert.Equal((1_000_000, 5), (history.Cycles[1].Points, history.Cycles[1].Referrals));
        Assert.Single(history.Cycles[1].Redemptions);
    }

    // A completion caused by service points or a referral reward is recorded at
    // the time of that earning transaction, so on every read its reset is the
    // later record and the completed cycle ends exactly then.
    [SqlServerIntegrationFact]
    public async Task CompletingServicePoints_AndTheirReset_AreOrderedChronologically()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var data = await SeedWithUnlockedRewardRedeemedAsync(database, 950_000, 5);

        await RecordServiceAsync(database, data, data.PatientA.Id);

        await AssertCompletedByAsync(database, data, LoyaltyTransactionType.ServicePointsEarned);
    }

    [SqlServerIntegrationFact]
    public async Task CompletingReferralReward_AndItsReset_AreOrderedChronologically()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var data = await SeedWithUnlockedRewardRedeemedAsync(database, 1_000_000, 4);

        await ReferAndValidateAsync(database, data, data.PatientA.Id, data.PatientB.Id);

        await AssertCompletedByAsync(database, data, LoyaltyTransactionType.ReferralPointsEarned);
    }

    // Patient A with the given balances, having redeemed the only reward - so the
    // next earning that reaches both targets completes the cycle (§5.2).
    private static async Task<LoyaltyAccountTestData> SeedWithUnlockedRewardRedeemedAsync(
        SqlServerTestDatabase database,
        int points,
        int referrals)
    {
        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
            (await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id)).WithBalances(points, referrals);
            await context.SaveChangesAsync();
        }

        var reward = await SeedRewardAsync(database, data.Clinic.Id, "Peskiranje", 300_000, 1);
        Assert.False((await RedeemAsync(database, data, data.PatientA.Id, reward.Id)).CycleCompleted);
        return data;
    }

    private static async Task AssertCompletedByAsync(
        SqlServerTestDatabase database,
        LoyaltyAccountTestData data,
        LoyaltyTransactionType trigger)
    {
        for (var i = 0; i < 3; i++)
        {
            var page = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id)).Transactions!;
            Assert.Equal(
                new[] { LoyaltyTransactionType.CycleReset, trigger, LoyaltyTransactionType.RewardRedeemed },
                page.Items.Select(t => t.Type));
            Assert.Equal(page.Items[0].OccurredAt, page.Items[1].OccurredAt);
            Assert.Equal((-1_000_000, -5, LoyaltyCycleStatus.Completed),
                (page.Items[0].PointsChange, page.Items[0].ReferralChange, page.Items[0].EndedCycleStatus));
        }

        var transactions = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id)).Transactions!;
        var history = await ReadHistoryAsync(database, data.Clinic.Id, LoyaltyHistorySubject.Patient(data.PatientA.Id));
        Assert.Equal(new[] { LoyaltyCycleStatus.Active, LoyaltyCycleStatus.Completed }, history!.Cycles.Select(c => c.Status));
        Assert.Equal(transactions.Items[1].OccurredAt, history.Cycles[1].EndedAt);
        Assert.Equal(transactions.Items[1].OccurredAt, history.Cycles[0].StartedAt);
    }

    [SqlServerIntegrationFact]
    public async Task Transactions_ArePagedWithOffsetFetch_WithoutGapsOrRepeats()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
        }

        for (var i = 0; i < 5; i++)
        {
            await RecordServiceAsync(database, data, data.PatientA.Id);
        }

        var all = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id, 1, 100)).Transactions!.Items;
        var paged = new List<LoyaltyTransactionRecord>();

        for (var page = 1; page <= 3; page++)
        {
            var result = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id, page, 2)).Transactions!;
            Assert.Equal(5, result.TotalCount);
            paged.AddRange(result.Items);
        }

        Assert.Equal(5, all.Count);
        Assert.Equal(all, paged);
        Assert.Equal(all.OrderByDescending(t => t.OccurredAt), all);
    }

    // An expired cycle is shown rolled over, and its pending reset leads the
    // first page, while no row - not even a rowversion - changes.
    [SqlServerIntegrationFact]
    public async Task ReadingHistory_OfAnExpiredCycle_WritesNothing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
        }

        await RecordServiceAsync(database, data, data.PatientA.Id);
        await RecordServiceAsync(database, data, data.PatientA.Id);
        var start = DateTime.UtcNow.AddYears(-1).AddHours(-1);
        await using (var context = database.CreateContext())
        {
            (await context.LoyaltyCycles.SingleAsync(c => c.Id == data.CycleA.Id)).StartedOn(start);
            await context.SaveChangesAsync();
        }

        var before = await SnapshotAsync(database);

        var history = await ReadHistoryAsync(database, data.Clinic.Id, LoyaltyHistorySubject.Patient(data.PatientA.Id));
        var page1 = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id, 1, 2)).Transactions!;
        var page2 = (await ReadTransactionsAsync(database, data.Clinic.Id, data.PatientA.Id, 2, 2)).Transactions!;

        Assert.Equal(before, await SnapshotAsync(database));

        Assert.Equal((0, 0), (history!.Status.Points, history.Status.ReferralBalance));
        Assert.Equal(new[] { LoyaltyCycleStatus.Active, LoyaltyCycleStatus.Expired }, history.Cycles.Select(c => c.Status));
        Assert.Equal((150_000, 0), (history.Cycles[1].Points, history.Cycles[1].Referrals));
        Assert.Equal(history.Cycles[1].EndedAt, history.Cycles[0].StartedAt);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(
            new[] { LoyaltyTransactionType.CycleReset, LoyaltyTransactionType.ServicePointsEarned },
            page1.Items.Select(t => t.Type));
        Assert.Equal(-150_000, page1.Items[0].PointsChange);
        Assert.Equal(LoyaltyTransactionType.ServicePointsEarned, Assert.Single(page2.Items).Type);
    }

    [SqlServerIntegrationFact]
    public async Task History_IsScopedToTheCallersClinic()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        LoyaltyAccountTestData data;
        Clinic otherClinic;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
            otherClinic = new Clinic("Clinic B");
            context.Clinics.Add(otherClinic);
            await context.SaveChangesAsync();
        }

        await RecordServiceAsync(database, data, data.PatientA.Id);

        Assert.Null(await ReadHistoryAsync(database, otherClinic.Id, LoyaltyHistorySubject.Patient(data.PatientA.Id)));
        Assert.Null(await ReadHistoryAsync(database, otherClinic.Id, LoyaltyHistorySubject.CurrentPatient, data.PatientA.UserId));
        Assert.Equal(
            GetLoyaltyTransactionsStatus.PatientNotFound,
            (await ReadTransactionsAsync(database, otherClinic.Id, data.PatientA.Id)).Status);

        await using var readContext = database.CreateContext();
        var repository = new LoyaltyHistoryRepository(readContext);
        Assert.Equal(0, await repository.CountTransactionsAsync(data.PatientA.Id, otherClinic.Id));
        Assert.Empty(await repository.ListTransactionsAsync(data.PatientA.Id, otherClinic.Id, 0, 10));
        Assert.Empty(await repository.ListCyclesAsync(data.PatientA.Id, otherClinic.Id));
    }
}
