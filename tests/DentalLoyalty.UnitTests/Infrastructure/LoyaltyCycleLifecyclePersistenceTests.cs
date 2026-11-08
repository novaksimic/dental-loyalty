using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.Application.Referrals.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// The loyalty cycle lifecycle (BUSINESS.md §5.2-§5.4; ADR-008) through the real
// handlers and repositories on a real relational engine (in-memory SQLite)
// built from the production configurations - including the one-active-cycle
// and one-reset-per-cycle unique indexes. Every repository shares one context
// per operation, as in an API request.
//
// SQLite has no rowversion, so this context emulates it for LoyaltyAccounts and
// Referrals, as the other persistence tests do; SQL Server behaviour is proven
// by SqlServerLoyaltyCycleLifecycleTests.
public sealed class LoyaltyCycleLifecyclePersistenceTests : IDisposable
{
    private static readonly string[] RowVersionTables = ["LoyaltyAccounts", "Referrals"];

    private readonly SqliteConnection _connection;

    public LoyaltyCycleLifecyclePersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();

        foreach (var table in RowVersionTables)
        {
            context.Database.ExecuteSqlRaw(
                $"""
                CREATE TRIGGER TR_{table}_RowVersion AFTER UPDATE ON {table}
                BEGIN
                    UPDATE {table} SET RowVersion = randomblob(8) WHERE rowid = NEW.rowid;
                END;
                """);
        }
    }

    public void Dispose() => _connection.Dispose();

    private SqliteRowVersionContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new SqliteRowVersionContext(options);
    }

    // --- arrange --------------------------------------------------------------

    private async Task<LoyaltyAccountTestData> SeedAsync()
    {
        await using var context = CreateContext();
        return await LoyaltyAccountTestData.SeedAsync(context);
    }

    private async Task<Reward> SeedRewardAsync(Guid clinicId, int points, int referrals, bool active = true)
    {
        await using var context = CreateContext();
        var reward = new Reward(clinicId, $"Reward {points}/{referrals}", points, referrals);

        if (!active)
        {
            reward.Deactivate();
        }

        context.Rewards.Add(reward);
        await context.SaveChangesAsync();
        return reward;
    }

    private async Task DeactivateRewardAsync(Guid rewardId)
    {
        await using var context = CreateContext();
        (await context.Rewards.SingleAsync(r => r.Id == rewardId)).Deactivate();
        await context.SaveChangesAsync();
    }

    private async Task SetBalancesAsync(Guid patientId, int points, int referrals)
    {
        await using var context = CreateContext();
        (await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == patientId)).WithBalances(points, referrals);
        await context.SaveChangesAsync();
    }

    private async Task StartCycleOnAsync(Guid cycleId, DateTime startedAt)
    {
        await using var context = CreateContext();
        (await context.LoyaltyCycles.SingleAsync(c => c.Id == cycleId)).StartedOn(startedAt);
        await context.SaveChangesAsync();
    }

    // --- act: one API request each --------------------------------------------

    private async Task<RedeemRewardResult> RedeemAsync(LoyaltyAccountTestData data, Guid patientId, Guid rewardId, Guid? clinicId = null)
    {
        await using var context = CreateContext();
        var accounts = new LoyaltyAccountRepository(context);
        return await new RedeemRewardHandler(
                new PatientRepository(context),
                new RewardRepository(context),
                accounts,
                LoyaltyCycleProgressionTestFactory.Create(context, accounts),
                FakeCurrentTenant.ForClinic(clinicId ?? data.Clinic.Id),
                FakeCurrentUser.For(data.Admin.Id))
            .HandleAsync(new RedeemRewardCommand(patientId, rewardId));
    }

    private async Task<RecordPatientServiceResult> RecordServiceAsync(LoyaltyAccountTestData data, Guid patientId, Guid? clinicId = null)
    {
        await using var context = CreateContext();
        var accounts = new LoyaltyAccountRepository(context);
        return await new RecordPatientServiceHandler(
                new PatientRepository(context),
                new ServiceRepository(context),
                new LoyaltyConfigurationRepository(context),
                accounts,
                LoyaltyCycleProgressionTestFactory.Create(context, accounts),
                new PatientServiceRepository(context),
                FakeCurrentTenant.ForClinic(clinicId ?? data.Clinic.Id),
                FakeCurrentUser.For(data.Admin.Id))
            .HandleAsync(new RecordPatientServiceCommand(patientId, Guid.NewGuid(), data.Service.Id, null));
    }

    private async Task<ValidateReferralResult> ReferAndValidateAsync(LoyaltyAccountTestData data, Patient referrer, Patient referred)
    {
        Guid referralId;
        await using (var context = CreateContext())
        {
            var referral = Referral.Record(referrer, referred, data.Admin.Id);
            context.Referrals.Add(referral);
            await context.SaveChangesAsync();
            referralId = referral.Id;
        }

        await using var validationContext = CreateContext();
        var accounts = new LoyaltyAccountRepository(validationContext);
        return await new ValidateReferralHandler(
                new ReferralRepository(validationContext),
                new PatientRepository(validationContext),
                accounts,
                new LoyaltyCycleRepository(validationContext),
                LoyaltyCycleProgressionTestFactory.Create(validationContext, accounts),
                FakeCurrentTenant.ForClinic(data.Clinic.Id),
                FakeCurrentUser.For(data.Admin.Id))
            .HandleAsync(new ValidateReferralCommand(referralId, null));
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

    // A new patient of the clinic, registered and linked, with an active cycle.
    private async Task<Patient> SeedLinkedPatientAsync(Guid clinicId)
    {
        await using var context = CreateContext();
        var user = new User(clinicId, $"iva-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);
        var patient = new Patient(clinicId, "Iva", "Ilic", "+381600000002");
        patient.LinkUser(user.Id);
        context.Users.Add(user);
        context.Patients.Add(patient);
        context.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));
        context.LoyaltyCycles.Add(LoyaltyCycle.StartFirst(patient));
        await context.SaveChangesAsync();
        return patient;
    }

    // --- assert ---------------------------------------------------------------

    private async Task<List<LoyaltyCycle>> ReadCyclesAsync(Guid patientId)
    {
        await using var context = CreateContext();
        return await context.LoyaltyCycles.AsNoTracking()
            .Where(c => c.PatientId == patientId)
            .OrderBy(c => c.StartedAt)
            .ToListAsync();
    }

    private async Task<(int Points, int Referrals)> ReadBalancesAsync(Guid patientId)
    {
        await using var context = CreateContext();
        var account = await context.LoyaltyAccounts.AsNoTracking().SingleAsync(a => a.PatientId == patientId);
        return (account.Points, account.ReferralBalance);
    }

    private async Task<List<LoyaltyTransaction>> ReadTransactionsAsync(Guid patientId)
    {
        await using var context = CreateContext();
        return await context.LoyaltyTransactions.AsNoTracking()
            .Where(t => t.PatientId == patientId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();
    }

    // Exactly one Completed cycle followed by an active one starting at its end,
    // with the balances reset and a single CycleReset recording it.
    private async Task AssertCompletedOnceAsync(Guid patientId, int resetPoints, int resetReferrals)
    {
        var cycles = await ReadCyclesAsync(patientId);
        Assert.Equal(2, cycles.Count);
        Assert.Equal(LoyaltyCycleStatus.Completed, cycles[0].Status);
        Assert.NotNull(cycles[0].EndedAt);
        Assert.Equal(LoyaltyCycleStatus.Active, cycles[1].Status);
        Assert.Equal(cycles[0].EndedAt, cycles[1].StartedAt);
        Assert.Null(cycles[1].EndedAt);

        Assert.Equal((0, 0), await ReadBalancesAsync(patientId));
        var reset = Assert.Single(await ReadTransactionsAsync(patientId), t => t.Type == LoyaltyTransactionType.CycleReset);
        Assert.Equal((-resetPoints, -resetReferrals, cycles[0].Id), (reset.PointsChange, reset.ReferralChange, reset.LoyaltyCycleId));
    }

    // The operation runs at one instant: the service is recorded - or the
    // referral validated - and credited at it, and the completion it causes,
    // the reset and the start of the next cycle are recorded at it too
    // (BUSINESS.md §5.2, §5.4, §14).
    private async Task AssertCompletedAtTriggerAsync(Guid patientId, LoyaltyTransactionType trigger)
    {
        var cycles = await ReadCyclesAsync(patientId);
        var transactions = await ReadTransactionsAsync(patientId);
        var earned = Assert.Single(transactions, t => t.Type == trigger);
        var reset = Assert.Single(transactions, t => t.Type == LoyaltyTransactionType.CycleReset);

        Assert.Equal(earned.CreatedAt, reset.CreatedAt);
        Assert.Equal(earned.CreatedAt, cycles[0].EndedAt);
        Assert.Equal(earned.CreatedAt, cycles[1].StartedAt);

        await using var context = CreateContext();
        DateTime? operationTime = trigger == LoyaltyTransactionType.ServicePointsEarned
            ? await context.PatientServices.AsNoTracking()
                .Where(ps => ps.Id == earned.PatientServiceId)
                .Select(ps => (DateTime?)ps.RecordedAt)
                .SingleAsync()
            : await context.Referrals.AsNoTracking()
                .Where(r => r.Id == earned.ReferralId)
                .Select(r => r.CompletedAt)
                .SingleAsync();
        Assert.Equal(earned.CreatedAt, operationTime);
    }

    private async Task AssertStillActiveAsync(Guid patientId)
    {
        var cycle = Assert.Single(await ReadCyclesAsync(patientId));
        Assert.Equal(LoyaltyCycleStatus.Active, cycle.Status);
        Assert.DoesNotContain(await ReadTransactionsAsync(patientId), t => t.Type == LoyaltyTransactionType.CycleReset);
    }

    private void FailInsertsInto(string table)
    {
        // table is always a table name written in this test class, never input.
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw(
            $"""
            CREATE TRIGGER TR_Fail_{table} BEFORE INSERT ON {table}
            BEGIN
                SELECT RAISE(ABORT, 'forced failure');
            END;
            """);
    }

    // --- completion: each triggering operation --------------------------------

    // BUSINESS.md §5.2, §19 step 5: redeeming the last unlocked reward completes
    // the cycle in the same operation. The redemption itself stays recorded in
    // - and linked to - the completed cycle.
    [Fact]
    public async Task RedeemingTheLastUnlockedReward_CompletesTheCycle()
    {
        var data = await SeedAsync();
        var tier1 = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        var tier3 = await SeedRewardAsync(data.Clinic.Id, 1_000_000, 5);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);

        var first = await RedeemAsync(data, data.PatientA.Id, tier1.Id);
        var last = await RedeemAsync(data, data.PatientA.Id, tier3.Id);

        Assert.False(first.CycleCompleted);
        Assert.True(last.CycleCompleted);
        await AssertCompletedOnceAsync(data.PatientA.Id, 1_000_000, 5);

        await using var context = CreateContext();
        var redemptions = await context.RewardRedemptions.AsNoTracking().Where(r => r.PatientId == data.PatientA.Id).ToListAsync();
        Assert.Equal(2, redemptions.Count);
        Assert.All(redemptions, r => Assert.Equal(data.CycleA.Id, r.LoyaltyCycleId));
    }

    // Points that reach the target, when the referrals and every unlocked reward
    // are already done, complete the cycle in the same operation as the points.
    [Fact]
    public async Task ServicePointsReachingTheTarget_CompleteTheCycle()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 950_000, 5);
        Assert.False((await RedeemAsync(data, data.PatientA.Id, reward.Id)).CycleCompleted);

        var result = await RecordServiceAsync(data, data.PatientA.Id);

        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, result.Status);
        Assert.Equal(50_000, result.CreditedPoints);
        Assert.True(result.CycleCompleted);
        await AssertCompletedOnceAsync(data.PatientA.Id, 1_000_000, 5);
        await AssertCompletedAtTriggerAsync(data.PatientA.Id, LoyaltyTransactionType.ServicePointsEarned);
    }

    // The fifth referral reward completes the referrer's cycle; the referred
    // patient's cycle is unaffected.
    [Fact]
    public async Task TheFifthReferral_CompletesTheReferrersCycle()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 4);
        await RedeemAsync(data, data.PatientA.Id, reward.Id);
        var newPatient = await SeedLinkedPatientAsync(data.Clinic.Id);

        var result = await ReferAndValidateAsync(data, data.PatientA, newPatient);

        Assert.Equal(new ReferralRewardResult(ReferralRewardOutcome.Rewarded, 0, CycleCompleted: true), result.Referrer);
        Assert.Equal(new ReferralRewardResult(ReferralRewardOutcome.Rewarded, 50_000, CycleCompleted: false), result.Referred);
        await AssertCompletedOnceAsync(data.PatientA.Id, 1_000_000, 5);
        await AssertCompletedAtTriggerAsync(data.PatientA.Id, LoyaltyTransactionType.ReferralPointsEarned);
        await AssertStillActiveAsync(newPatient.Id);
    }

    // --- incomplete cycles ----------------------------------------------------

    [Fact]
    public async Task PointsReached_ReferralsMissing_TheCycleContinues()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 4);

        Assert.False((await RedeemAsync(data, data.PatientA.Id, reward.Id)).CycleCompleted);

        await AssertStillActiveAsync(data.PatientA.Id);
        Assert.Equal((1_000_000, 4), await ReadBalancesAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task ReferralsReached_PointsMissing_TheCycleContinues()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 900_000, 5);

        Assert.False((await RedeemAsync(data, data.PatientA.Id, reward.Id)).CycleCompleted);

        await AssertStillActiveAsync(data.PatientA.Id);
    }

    [Fact]
    public async Task BothThresholdsReached_ARewardStillAvailable_TheCycleContinues()
    {
        var data = await SeedAsync();
        var tier1 = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SeedRewardAsync(data.Clinic.Id, 400_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);

        Assert.False((await RedeemAsync(data, data.PatientA.Id, tier1.Id)).CycleCompleted);

        await AssertStillActiveAsync(data.PatientA.Id);
        Assert.Equal((1_000_000, 5), await ReadBalancesAsync(data.PatientA.Id));
    }

    // ADR-008: an unlocked reward the clinic withdrew does not block completion.
    [Fact]
    public async Task AWithdrawnUnredeemedReward_DoesNotBlockCompletion()
    {
        var data = await SeedAsync();
        var tier1 = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        var withdrawn = await SeedRewardAsync(data.Clinic.Id, 400_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);
        await DeactivateRewardAsync(withdrawn.Id);

        Assert.True((await RedeemAsync(data, data.PatientA.Id, tier1.Id)).CycleCompleted);

        await AssertCompletedOnceAsync(data.PatientA.Id, 1_000_000, 5);
    }

    // After completion the next cycle starts from zero: rewards are evaluated
    // again (§5.4) and the one redeemed in the previous cycle can be earned anew.
    [Fact]
    public async Task AfterCompletion_RewardsAreLockedAgainInTheNextCycle()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);
        await RedeemAsync(data, data.PatientA.Id, reward.Id);

        var view = await GetRewardsAsync(data.Clinic.Id, data.PatientA.Id);

        Assert.Equal((0, 0), (view!.Points, view.ReferralBalance));
        Assert.Equal(RewardStatus.Locked, Assert.Single(view.Rewards).Status);
    }

    // --- expiry ---------------------------------------------------------------

    // BUSINESS.md §5.3, §5.4: the next operation ends the expired cycle - at its
    // one-year end, as Expired, never as Completed - and starts the next one
    // there, resetting progress. Earlier history is left untouched.
    [Fact]
    public async Task ExpiredCycleWithIncompleteRequirements_IsExpired_AndTheNextStartsAtItsEnd()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 600_000, 2);
        await RedeemAsync(data, data.PatientA.Id, reward.Id);
        var historyBefore = await ReadTransactionsAsync(data.PatientA.Id);
        var start = DateTime.UtcNow.AddYears(-1).AddHours(-1);
        await StartCycleOnAsync(data.CycleA.Id, start);

        var result = await RecordServiceAsync(data, data.PatientA.Id);

        var cycles = await ReadCyclesAsync(data.PatientA.Id);
        Assert.Equal(2, cycles.Count);
        Assert.Equal(LoyaltyCycleStatus.Expired, cycles[0].Status);
        Assert.Equal(start.AddYears(1), cycles[0].EndedAt);
        Assert.Equal(LoyaltyCycleStatus.Active, cycles[1].Status);
        Assert.Equal(start.AddYears(1), cycles[1].StartedAt);

        // The new service was performed in the new cycle and earns there.
        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, result.Status);
        Assert.Equal((75_000, 0), await ReadBalancesAsync(data.PatientA.Id));

        var history = await ReadTransactionsAsync(data.PatientA.Id);
        Assert.All(historyBefore, before => Assert.Contains(history, t =>
            t.Id == before.Id && t.PointsChange == before.PointsChange && t.ReferralChange == before.ReferralChange));
        var reset = Assert.Single(history, t => t.Type == LoyaltyTransactionType.CycleReset);
        Assert.Equal((-600_000, -2, cycles[0].Id), (reset.PointsChange, reset.ReferralChange, reset.LoyaltyCycleId));

        await using var context = CreateContext();
        var redemption = await context.RewardRedemptions.AsNoTracking().SingleAsync(r => r.PatientId == data.PatientA.Id);
        Assert.Equal(cycles[0].Id, redemption.LoyaltyCycleId);
    }

    [Fact]
    public async Task JustBeforeTheOneYearEnd_TheCycleIsStillActive()
    {
        var data = await SeedAsync();
        await StartCycleOnAsync(data.CycleA.Id, DateTime.UtcNow.AddYears(-1).AddMinutes(5));

        await RecordServiceAsync(data, data.PatientA.Id);

        await AssertStillActiveAsync(data.PatientA.Id);
        Assert.Equal((75_000, 0), await ReadBalancesAsync(data.PatientA.Id));
    }

    // Inactive for more than two years: both missed cycles expire in turn.
    [Fact]
    public async Task AfterYearsOfInactivity_EveryMissedCycleExpires()
    {
        var data = await SeedAsync();
        var start = DateTime.UtcNow.AddYears(-2).AddDays(-1);
        await StartCycleOnAsync(data.CycleA.Id, start);

        await RecordServiceAsync(data, data.PatientA.Id);

        var cycles = await ReadCyclesAsync(data.PatientA.Id);
        Assert.Equal(
            new[] { LoyaltyCycleStatus.Expired, LoyaltyCycleStatus.Expired, LoyaltyCycleStatus.Active },
            cycles.Select(c => c.Status));
        Assert.Equal(new[] { start, start.AddYears(1), start.AddYears(2) }, cycles.Select(c => c.StartedAt));
        Assert.Equal(2, (await ReadTransactionsAsync(data.PatientA.Id)).Count(t => t.Type == LoyaltyTransactionType.CycleReset));
    }

    // A read shows the state as of now but never saves the rollover.
    [Fact]
    public async Task ReadingAnExpiredCycle_ShowsTheNextCycle_WithoutSavingIt()
    {
        var data = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 2);
        await StartCycleOnAsync(data.CycleA.Id, DateTime.UtcNow.AddYears(-1).AddHours(-1));

        var view = await GetRewardsAsync(data.Clinic.Id, data.PatientA.Id);

        Assert.Equal((0, 0), (view!.Points, view.ReferralBalance));
        await AssertStillActiveAsync(data.PatientA.Id);
        Assert.Equal((600_000, 2), await ReadBalancesAsync(data.PatientA.Id));
    }

    // Expiry is recorded even when the operation that found it is rejected:
    // it happened at the one-year end regardless.
    [Fact]
    public async Task ExpiryIsRecorded_EvenWhenTheTriggeringRedemptionIsRejected()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 600_000, 2);
        await StartCycleOnAsync(data.CycleA.Id, DateTime.UtcNow.AddYears(-1).AddHours(-1));

        var result = await RedeemAsync(data, data.PatientA.Id, reward.Id);

        Assert.Equal(RedeemRewardStatus.NotEligible, result.Status);
        Assert.Equal(
            new[] { LoyaltyCycleStatus.Expired, LoyaltyCycleStatus.Active },
            (await ReadCyclesAsync(data.PatientA.Id)).Select(c => c.Status));
        Assert.Equal((0, 0), await ReadBalancesAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task AnotherClinic_CannotTriggerTheRollover()
    {
        var data = await SeedAsync();
        await StartCycleOnAsync(data.CycleA.Id, DateTime.UtcNow.AddYears(-1).AddHours(-1));

        var result = await RecordServiceAsync(data, data.PatientA.Id, clinicId: Guid.NewGuid());

        Assert.Equal(RecordPatientServiceStatus.PatientNotFound, result.Status);
        await AssertStillActiveAsync(data.PatientA.Id);
    }

    // --- concurrency and atomicity --------------------------------------------

    // Two operations that both found the cycle complete (or expired) before
    // either saved: the second fails and persists nothing, so the cycle ends
    // once and there is one next cycle.
    [Fact]
    public async Task ConcurrentCompletions_CompleteTheCycleOnce()
    {
        var data = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);

        await using var first = CreateContext();
        await using var second = CreateContext();
        var (firstAccounts, firstAccount) = await PrepareCompletionAsync(first, data);
        var (secondAccounts, secondAccount) = await PrepareCompletionAsync(second, data);

        await firstAccounts.SaveBalanceChangesAsync([firstAccount], []);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondAccounts.SaveBalanceChangesAsync([secondAccount], []));

        await AssertCompletedOnceAsync(data.PatientA.Id, 1_000_000, 5);
    }

    [Fact]
    public async Task ConcurrentRollovers_ExpireTheCycleOnce()
    {
        var data = await SeedAsync();
        await StartCycleOnAsync(data.CycleA.Id, DateTime.UtcNow.AddYears(-1).AddHours(-1));

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstAccounts = new LoyaltyAccountRepository(first);
        var secondAccounts = new LoyaltyAccountRepository(second);
        var firstAccount = (await firstAccounts.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var secondAccount = (await secondAccounts.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        await LoyaltyCycleProgressionTestFactory.Create(first, firstAccounts).GetCurrentCycleAsync(firstAccount, DateTime.UtcNow, save: false);
        await LoyaltyCycleProgressionTestFactory.Create(second, secondAccounts).GetCurrentCycleAsync(secondAccount, DateTime.UtcNow, save: false);

        await firstAccounts.SaveBalanceChangesAsync([firstAccount], []);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondAccounts.SaveBalanceChangesAsync([secondAccount], []));

        Assert.Equal(
            new[] { LoyaltyCycleStatus.Expired, LoyaltyCycleStatus.Active },
            (await ReadCyclesAsync(data.PatientA.Id)).Select(c => c.Status));
    }

    // Even when the account version check is defeated, the one-active-cycle and
    // one-reset-per-cycle unique indexes stop a duplicate next cycle.
    [Fact]
    public async Task DuplicateNextCycle_IsRejectedByTheDatabase()
    {
        var data = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);

        await using var stale = CreateContext();
        var (staleAccounts, staleAccount) = await PrepareCompletionAsync(stale, data);

        await using (var context = CreateContext())
        {
            var (accounts, account) = await PrepareCompletionAsync(context, data);
            await accounts.SaveBalanceChangesAsync([account], []);
        }

        var current = await stale.LoyaltyAccounts.AsNoTracking().Where(a => a.Id == staleAccount.Id).Select(a => a.RowVersion).SingleAsync();
        stale.Entry(staleAccount).Property(a => a.RowVersion).OriginalValue = current;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            staleAccounts.SaveBalanceChangesAsync([staleAccount], []));

        await AssertCompletedOnceAsync(data.PatientA.Id, 1_000_000, 5);
    }

    // An operation that read the account before the cycle completed fails, so
    // nothing is recorded against the completed cycle - even at the maximum
    // balance, where the service credits 0 points and changes no balance.
    [Fact]
    public async Task ServicePointsDecidedBeforeACompletion_Conflict()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);

        await using var context = CreateContext();
        var accounts = new LoyaltyAccountRepository(context);
        var account = (await accounts.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var patientService = data.RecordService(data.PatientA);
        var earning = account.EarnServicePoints(patientService, data.CycleA);

        Assert.True((await RedeemAsync(data, data.PatientA.Id, reward.Id)).CycleCompleted);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            accounts.SaveServicePointsAsync(patientService, account, earning));

        await AssertCompletedOnceAsync(data.PatientA.Id, 1_000_000, 5);
    }

    // If any part of a completing redemption fails, nothing is saved: no
    // redemption, no completion, no reset (BUSINESS.md §13.1, §19).
    [Fact]
    public async Task WhenStartingTheNextCycleFails_TheCompletingRedemptionIsRolledBack()
    {
        var data = await SeedAsync();
        var reward = await SeedRewardAsync(data.Clinic.Id, 300_000, 1);
        await SetBalancesAsync(data.PatientA.Id, 1_000_000, 5);
        FailInsertsInto("LoyaltyCycles");

        await Assert.ThrowsAsync<DbUpdateException>(() => RedeemAsync(data, data.PatientA.Id, reward.Id));

        await AssertStillActiveAsync(data.PatientA.Id);
        Assert.Equal((1_000_000, 5), await ReadBalancesAsync(data.PatientA.Id));
        Assert.Empty(await ReadTransactionsAsync(data.PatientA.Id));
        await using var context = CreateContext();
        Assert.Empty(await context.RewardRedemptions.Where(r => r.PatientId == data.PatientA.Id).ToListAsync());
    }

    [Fact]
    public async Task WhenStartingTheNextCycleFails_TheExpiryIsRolledBack()
    {
        var data = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 600_000, 2);
        await StartCycleOnAsync(data.CycleA.Id, DateTime.UtcNow.AddYears(-1).AddHours(-1));
        FailInsertsInto("LoyaltyCycles");

        await Assert.ThrowsAsync<DbUpdateException>(() => RecordServiceAsync(data, data.PatientA.Id));

        await AssertStillActiveAsync(data.PatientA.Id);
        Assert.Equal((600_000, 2), await ReadBalancesAsync(data.PatientA.Id));
    }

    // In the given unit of work: the patient's account, completed by the lifecycle
    // without being saved (no reward is available, the thresholds are met).
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
                account.ToTable(table => table.HasTrigger("TR_LoyaltyAccounts_RowVersion"));
                account.Property(a => a.RowVersion).HasDefaultValueSql("randomblob(8)");
            });

            modelBuilder.Entity<Referral>(referral =>
            {
                referral.ToTable(table => table.HasTrigger("TR_Referrals_RowVersion"));
                referral.Property(r => r.RowVersion).HasDefaultValueSql("randomblob(8)");
            });
        }
    }
}
