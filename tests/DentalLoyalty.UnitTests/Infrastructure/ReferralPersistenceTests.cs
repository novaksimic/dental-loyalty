using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Loyalty.Queries;
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

// Referral recording and validation (BUSINESS.md §4, §5.1; ADR-007) through
// CreateReferralHandler, ValidateReferralHandler and the real repositories on a
// real relational engine (in-memory SQLite) built from the production
// configurations. Every repository shares one context per operation, as in an
// API request.
//
// SQLite has no rowversion, so this context emulates it for LoyaltyAccounts and
// Referrals (a database default plus an update trigger), as
// RedeemRewardPersistenceTests does; SQL Server behaviour is proven by
// SqlServerReferralValidationTests.
public sealed class ReferralPersistenceTests : IDisposable
{
    private static readonly string[] RowVersionTables = ["LoyaltyAccounts", "Referrals"];

    private readonly SqliteConnection _connection;

    public ReferralPersistenceTests()
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

    private async Task<LoyaltyAccountTestData> SeedAsync()
    {
        await using var context = CreateContext();
        return await LoyaltyAccountTestData.SeedAsync(context);
    }

    // A patient of the clinic with an account but not registered or linked - so
    // with no loyalty cycle (BUSINESS.md §5.1).
    private async Task<Patient> SeedUnlinkedPatientAsync(Guid clinicId)
    {
        await using var context = CreateContext();
        var patient = new Patient(clinicId, "Iva", "Ilic", "+381600000002");
        context.Patients.Add(patient);
        context.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));
        await context.SaveChangesAsync();
        return patient;
    }

    private async Task StartCycleOnAsync(Guid cycleId, DateTime startedAt)
    {
        await using var context = CreateContext();
        (await context.LoyaltyCycles.SingleAsync(c => c.Id == cycleId)).StartedOn(startedAt);
        await context.SaveChangesAsync();
    }

    private async Task SetBalancesAsync(Guid patientId, int points, int referrals)
    {
        await using var context = CreateContext();
        var account = await context.LoyaltyAccounts.SingleAsync(a => a.PatientId == patientId);
        account.WithBalances(points, referrals);
        await context.SaveChangesAsync();
    }

    private async Task<CreateReferralResult> CreateAsync(
        LoyaltyAccountTestData data,
        Guid referrerId,
        Guid referredId,
        Guid? clinicId = null)
    {
        await using var context = CreateContext();
        return await new CreateReferralHandler(
                new PatientRepository(context),
                new ReferralRepository(context),
                FakeCurrentTenant.ForClinic(clinicId ?? data.Clinic.Id),
                FakeCurrentUser.For(data.Admin.Id))
            .HandleAsync(new CreateReferralCommand(referrerId, referredId));
    }

    private static ValidateReferralHandler CreateValidateHandler(DentalLoyaltyDbContext context, Guid? clinicId, Guid? userId)
    {
        var accounts = new LoyaltyAccountRepository(context);
        return new(
            new ReferralRepository(context),
            new PatientRepository(context),
            accounts,
            new LoyaltyCycleRepository(context),
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            clinicId is null ? FakeCurrentTenant.NoTenant() : FakeCurrentTenant.ForClinic(clinicId.Value),
            userId is null ? FakeCurrentUser.NoUser() : FakeCurrentUser.For(userId.Value));
    }

    private async Task<ValidateReferralResult> ValidateAsync(
        LoyaltyAccountTestData data,
        Guid referralId,
        DateTime? examinedAt = null,
        Guid? clinicId = null)
    {
        await using var context = CreateContext();
        return await CreateValidateHandler(context, clinicId ?? data.Clinic.Id, data.Admin.Id)
            .HandleAsync(new ValidateReferralCommand(referralId, examinedAt));
    }

    // A Pending referral from A to B, recorded through the handler.
    private async Task<Guid> CreateReferralAsync(LoyaltyAccountTestData data, Guid? referrerId = null, Guid? referredId = null)
    {
        var result = await CreateAsync(data, referrerId ?? data.PatientA.Id, referredId ?? data.PatientB.Id);
        Assert.Equal(CreateReferralStatus.Created, result.Status);
        return result.ReferralId!.Value;
    }

    private async Task<(int Points, int Referrals, int Transactions)> ReadStateAsync(Guid patientId)
    {
        await using var context = CreateContext();
        var account = await context.LoyaltyAccounts.AsNoTracking().SingleAsync(a => a.PatientId == patientId);
        var transactions = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId);
        return (account.Points, account.ReferralBalance, transactions);
    }

    private async Task<Referral> ReadReferralAsync(Guid referralId)
    {
        await using var context = CreateContext();
        return await context.Referrals.AsNoTracking().SingleAsync(r => r.Id == referralId);
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

    // --- recording ------------------------------------------------------------

    [Fact]
    public async Task Create_RecordsPendingReferral_WithoutChangingBalances()
    {
        var data = await SeedAsync();

        var referralId = await CreateReferralAsync(data);

        var referral = await ReadReferralAsync(referralId);
        Assert.Equal(ReferralStatus.Pending, referral.Status);
        Assert.Equal((data.PatientA.Id, data.PatientB.Id), (referral.ReferrerPatientId, referral.ReferredPatientId));
        Assert.Equal(data.Admin.Id, referral.CreatedBy);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientB.Id));
    }

    [Fact]
    public async Task Create_SelfReferral_IsRejected()
    {
        var data = await SeedAsync();

        var result = await CreateAsync(data, data.PatientA.Id, data.PatientA.Id);

        Assert.Equal(CreateReferralStatus.SelfReferral, result.Status);
    }

    // A patient is referred at most once - by anyone.
    [Fact]
    public async Task Create_ForAnAlreadyReferredPatient_IsRejected()
    {
        var data = await SeedAsync();
        var third = await SeedUnlinkedPatientAsync(data.Clinic.Id);
        await CreateReferralAsync(data);

        var again = await CreateAsync(data, data.PatientA.Id, data.PatientB.Id);
        var fromAnother = await CreateAsync(data, third.Id, data.PatientB.Id);

        Assert.Equal(CreateReferralStatus.AlreadyReferred, again.Status);
        Assert.Equal(CreateReferralStatus.AlreadyReferred, fromAnother.Status);
    }

    // The unique key stops a second referral that got past the handler's check.
    [Fact]
    public async Task SecondReferralOfTheSamePatient_IsRejectedByTheDatabase()
    {
        var data = await SeedAsync();
        await CreateReferralAsync(data);

        await using var context = CreateContext();
        var duplicate = Referral.Record(data.PatientA, data.PatientB, data.Admin.Id);

        await Assert.ThrowsAsync<DuplicateReferralException>(() =>
            new ReferralRepository(context).AddAsync(duplicate));
    }

    [Fact]
    public async Task Create_WithAPatientOfAnotherClinic_IsNotFound()
    {
        var data = await SeedAsync();
        Guid otherClinicPatientId;
        await using (var context = CreateContext())
        {
            otherClinicPatientId = (await LoyaltyAccountTestData.SeedAsync(context)).PatientA.Id;
        }

        var result = await CreateAsync(data, data.PatientA.Id, otherClinicPatientId);

        Assert.Equal(CreateReferralStatus.PatientNotFound, result.Status);
    }

    [Fact]
    public async Task Create_InAnotherClinicsTenant_IsNotFound()
    {
        var data = await SeedAsync();

        var result = await CreateAsync(data, data.PatientA.Id, data.PatientB.Id, clinicId: Guid.NewGuid());

        Assert.Equal(CreateReferralStatus.PatientNotFound, result.Status);
    }

    [Fact]
    public async Task Create_WithAnInactivePatient_IsRejected()
    {
        var data = await SeedAsync();
        await using (var context = CreateContext())
        {
            (await context.Patients.SingleAsync(p => p.Id == data.PatientB.Id)).Deactivate();
            await context.SaveChangesAsync();
        }

        var result = await CreateAsync(data, data.PatientA.Id, data.PatientB.Id);

        Assert.Equal(CreateReferralStatus.PatientInactive, result.Status);
    }

    // --- validation -----------------------------------------------------------

    // BUSINESS.md §4.2; ADR-007 §2.
    [Fact]
    public async Task Validate_BothRegistered_RewardsBoth_AndRecordsTheValidation()
    {
        var data = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 250_000, 0);
        var referralId = await CreateReferralAsync(data);

        var result = await ValidateAsync(data, referralId);

        Assert.Equal(ValidateReferralStatus.Validated, result.Status);
        Assert.Equal(new ReferralRewardResult(ReferralRewardOutcome.Rewarded, 50_000), result.Referrer);
        Assert.Equal(new ReferralRewardResult(ReferralRewardOutcome.Rewarded, 50_000), result.Referred);
        Assert.Equal((300_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientB.Id));

        var referral = await ReadReferralAsync(referralId);
        Assert.Equal(ReferralStatus.Completed, referral.Status);
        Assert.Equal(data.Admin.Id, referral.ValidatedBy);
        Assert.NotNull(referral.ExaminedAt);
        Assert.False(referral.RewardsForfeited);
    }

    // The referral credit unlocks the Tier 1 reward (BUSINESS.md §6, §10).
    [Fact]
    public async Task Validate_ReferralCredit_UnlocksTier1Reward()
    {
        var data = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 300_000, 0);
        Guid rewardId;
        await using (var context = CreateContext())
        {
            var reward = new Reward(data.Clinic.Id, "Peskiranje", 300_000, 1);
            context.Rewards.Add(reward);
            await context.SaveChangesAsync();
            rewardId = reward.Id;
        }

        await ValidateAsync(data, await CreateReferralAsync(data));

        await using var readContext = CreateContext();
        var accounts = new LoyaltyAccountRepository(readContext);
        var rewards = await new GetPatientRewardsHandler(
                new PatientRepository(readContext),
                new RewardRepository(readContext),
                accounts,
                LoyaltyCycleProgressionTestFactory.Create(readContext, accounts),
                FakeCurrentTenant.ForClinic(data.Clinic.Id))
            .HandleAsync(new GetPatientRewardsQuery(data.PatientA.Id));
        Assert.Equal(RewardStatus.Available, Assert.Single(rewards!.Rewards, r => r.RewardId == rewardId).Status);
    }

    [Fact]
    public async Task Validate_ReferredNotRegistered_ForfeitsForBoth_ButRecordsTheReferralAsValid()
    {
        var data = await SeedAsync();
        var unlinked = await SeedUnlinkedPatientAsync(data.Clinic.Id);
        var referralId = await CreateReferralAsync(data, referredId: unlinked.Id);

        var result = await ValidateAsync(data, referralId);

        Assert.Equal(ValidateReferralStatus.Validated, result.Status);
        Assert.Equal(ReferralRewardOutcome.Forfeited, result.Referrer!.Outcome);
        Assert.Equal(ReferralRewardOutcome.Forfeited, result.Referred!.Outcome);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((0, 0, 0), await ReadStateAsync(unlinked.Id));
        var referral = await ReadReferralAsync(referralId);
        Assert.Equal(ReferralStatus.Completed, referral.Status);
        Assert.True(referral.RewardsForfeited);
    }

    // Registering later does not grant a forfeited referral (BUSINESS.md §4.2):
    // the referral is no longer pending, so it can never be validated again.
    [Fact]
    public async Task ForfeitedReferral_IsNotRewardedAfterTheReferredPatientRegisters()
    {
        var data = await SeedAsync();
        var unlinked = await SeedUnlinkedPatientAsync(data.Clinic.Id);
        var referralId = await CreateReferralAsync(data, referredId: unlinked.Id);
        await ValidateAsync(data, referralId);

        await using (var context = CreateContext())
        {
            var patient = await context.Patients.SingleAsync(p => p.Id == unlinked.Id);
            var user = new User(data.Clinic.Id, $"iva-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);
            context.Users.Add(user);
            patient.LinkUser(user.Id);
            context.LoyaltyCycles.Add(LoyaltyCycle.StartFirst(patient));
            await context.SaveChangesAsync();
        }

        var again = await ValidateAsync(data, referralId);

        Assert.Equal(ValidateReferralStatus.NotPending, again.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((0, 0, 0), await ReadStateAsync(unlinked.Id));
    }

    // An examination before the referred patient registered generates nothing,
    // even though they are registered when the admin confirms it.
    [Fact]
    public async Task Validate_ExaminationBeforeRegistration_ForfeitsForBoth()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        var result = await ValidateAsync(data, referralId, examinedAt: data.CycleB.StartedAt.AddMinutes(-1));

        Assert.Equal(ReferralRewardOutcome.Forfeited, result.Referred!.Outcome);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientB.Id));
    }

    // ADR-007 §3: the maximum applies to each patient on their own.
    [Fact]
    public async Task Validate_ReferrerAtTheMaximum_RewardsOnlyTheReferred()
    {
        var data = await SeedAsync();
        await SetBalancesAsync(data.PatientA.Id, 250_000, LoyaltyAccount.MaximumReferralRewards);
        var referralId = await CreateReferralAsync(data);

        var result = await ValidateAsync(data, referralId);

        Assert.Equal(new ReferralRewardResult(ReferralRewardOutcome.MaximumReached), result.Referrer);
        Assert.Equal(new ReferralRewardResult(ReferralRewardOutcome.Rewarded, 50_000), result.Referred);
        Assert.Equal((250_000, 5, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientB.Id));
    }

    [Fact]
    public async Task Validate_Repeated_IsRejected_AndRewardsOnce()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);
        await ValidateAsync(data, referralId);

        var repeated = await ValidateAsync(data, referralId);

        Assert.Equal(ValidateReferralStatus.NotPending, repeated.Status);
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientB.Id));
    }

    [Fact]
    public async Task Validate_InAnotherClinicsTenant_IsNotFound_AndChangesNothing()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        var result = await ValidateAsync(data, referralId, clinicId: Guid.NewGuid());

        Assert.Equal(ValidateReferralStatus.ReferralNotFound, result.Status);
        Assert.Equal(ReferralStatus.Pending, (await ReadReferralAsync(referralId)).Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task Validate_WithoutTenantOrUser_FailsClosed()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        await using var context = CreateContext();
        var noTenant = await CreateValidateHandler(context, null, data.Admin.Id).HandleAsync(new ValidateReferralCommand(referralId, null));
        var noUser = await CreateValidateHandler(context, data.Clinic.Id, null).HandleAsync(new ValidateReferralCommand(referralId, null));

        Assert.Equal(ValidateReferralStatus.ReferralNotFound, noTenant.Status);
        Assert.Equal(ValidateReferralStatus.ReferralNotFound, noUser.Status);
        Assert.Equal(ReferralStatus.Pending, (await ReadReferralAsync(referralId)).Status);
    }

    [Fact]
    public async Task Validate_ExaminationInTheFuture_IsInvalid()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        var result = await ValidateAsync(data, referralId, examinedAt: DateTime.UtcNow.AddHours(1));

        Assert.Equal(ValidateReferralStatus.InvalidRequest, result.Status);
        Assert.Equal(ReferralStatus.Pending, (await ReadReferralAsync(referralId)).Status);
    }

    // BUSINESS.md §5.3, §5.4: the referred patient's expired cycle is ended and
    // the next started at its one-year end; an examination after that is
    // rewarded in the next cycle.
    [Fact]
    public async Task Validate_AfterACycleExpired_RewardsInTheNextCycle()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);
        await StartCycleOnAsync(data.CycleB.Id, DateTime.UtcNow.AddYears(-1).AddDays(-1));

        var result = await ValidateAsync(data, referralId);

        Assert.Equal(ReferralRewardOutcome.Rewarded, result.Referrer!.Outcome);
        Assert.Equal(ReferralRewardOutcome.Rewarded, result.Referred!.Outcome);
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
        // The CycleReset of the expired cycle and the referral reward.
        Assert.Equal((50_000, 1, 2), await ReadStateAsync(data.PatientB.Id));
    }

    // ADR-008: an examination in a cycle that has since ended is not credited to
    // the next one - for that patient only. Both were registered, so it is not
    // forfeited, and the other patient is rewarded.
    [Fact]
    public async Task Validate_ExaminationInAnEndedCycle_IsNotCreditedToThatPatient()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);
        await StartCycleOnAsync(data.CycleA.Id, DateTime.UtcNow.AddMonths(-6));
        await StartCycleOnAsync(data.CycleB.Id, DateTime.UtcNow.AddYears(-1).AddDays(-1));

        var result = await ValidateAsync(data, referralId, examinedAt: DateTime.UtcNow.AddDays(-2));

        Assert.Equal(ReferralRewardOutcome.Rewarded, result.Referrer!.Outcome);
        Assert.Equal(ReferralRewardOutcome.CycleEnded, result.Referred!.Outcome);
        Assert.False((await ReadReferralAsync(referralId)).RewardsForfeited);
        Assert.Equal((0, 0, 1), await ReadStateAsync(data.PatientB.Id));
    }

    [Fact]
    public async Task Validate_WithAnInactivePatient_IsRejected_AndRecordsNothing()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);
        await using (var context = CreateContext())
        {
            (await context.Patients.SingleAsync(p => p.Id == data.PatientA.Id)).Deactivate();
            await context.SaveChangesAsync();
        }

        var result = await ValidateAsync(data, referralId);

        Assert.Equal(ValidateReferralStatus.PatientInactive, result.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientB.Id));
    }

    // --- atomicity and concurrency --------------------------------------------

    // Two validations that both read the referral and accounts before either
    // saved: the second fails and persists nothing, so nobody is rewarded twice.
    [Fact]
    public async Task ConcurrentValidations_RewardOnce()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstHandler = CreateValidateHandler(first, data.Clinic.Id, data.Admin.Id);
        var (secondReferral, secondReferrer, secondReferred, secondTransactions) = await PrepareValidationAsync(second, data, referralId);

        await firstHandler.HandleAsync(new ValidateReferralCommand(referralId, null));

        var secondAttempt = await Record.ExceptionAsync(() =>
            new LoyaltyAccountRepository(second).SaveReferralValidationAsync(secondReferral, secondReferrer, secondReferred, secondTransactions));

        Assert.True(secondAttempt is ConcurrencyConflictException or ReferralAlreadyValidatedException);
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientB.Id));
    }

    // A forfeiting validation changes no balance, but still cannot be saved over
    // a concurrent validation of the same referral, which would otherwise mark
    // a rewarded referral as forfeited.
    [Fact]
    public async Task ConcurrentValidation_OfAnAlreadyValidatedReferral_Conflicts()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        await using var stale = CreateContext();
        var referral = await new ReferralRepository(stale).GetByIdForTenantAsync(referralId, data.Clinic.Id);
        var accounts = new LoyaltyAccountRepository(stale);
        var referrerAccount = (await accounts.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var referredAccount = (await accounts.GetByPatientIdForTenantAsync(data.PatientB.Id, data.Clinic.Id))!;
        var forfeit = referral!.Validate(
            new ReferralParticipant(referrerAccount, null, null),
            new ReferralParticipant(referredAccount, null, null),
            DateTime.UtcNow,
            data.Admin.Id).Transactions;

        await ValidateAsync(data, referralId);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            accounts.SaveReferralValidationAsync(referral, referrerAccount, referredAccount, forfeit));

        Assert.False((await ReadReferralAsync(referralId)).RewardsForfeited);
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // A validation decided on a balance that changed before it was saved fails:
    // whether the patient was at the referral maximum must be current.
    [Fact]
    public async Task ValidationDecidedOnAStaleAccount_Conflicts_AndPersistsNothing()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        await using var context = CreateContext();
        var (referral, referrer, referred, transactions) = await PrepareValidationAsync(context, data, referralId);

        await SetBalancesAsync(data.PatientA.Id, 0, LoyaltyAccount.MaximumReferralRewards);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new LoyaltyAccountRepository(context).SaveReferralValidationAsync(referral, referrer, referred, transactions));

        Assert.Equal((0, 5, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientB.Id));
        Assert.Equal(ReferralStatus.Pending, (await ReadReferralAsync(referralId)).Status);
    }

    // Even when every version check passes, the (ReferralId, PatientId) unique
    // key stops a second reward for the same referral.
    [Fact]
    public async Task SecondRewardForTheSameReferral_IsRejectedByTheDatabase()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);

        await using var context = CreateContext();
        var (referral, referrer, referred, transactions) = await PrepareValidationAsync(context, data, referralId);

        await ValidateAsync(data, referralId);

        // Defeat the version checks, leaving the unique key as the only guard.
        foreach (var entry in context.ChangeTracker.Entries().Where(e => e.Entity is LoyaltyAccount or Referral))
        {
            var current = await entry.GetDatabaseValuesAsync();
            entry.Property("RowVersion").OriginalValue = current!["RowVersion"];
        }

        await Assert.ThrowsAsync<ReferralAlreadyValidatedException>(() =>
            new LoyaltyAccountRepository(context).SaveReferralValidationAsync(referral, referrer, referred, transactions));

        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(data.PatientB.Id));
    }

    [Fact]
    public async Task WhenTheTransactionInsertFails_NothingIsRecorded()
    {
        var data = await SeedAsync();
        var referralId = await CreateReferralAsync(data);
        FailInsertsInto("LoyaltyTransactions");

        await Assert.ThrowsAsync<DbUpdateException>(() => ValidateAsync(data, referralId));

        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientB.Id));
        Assert.Equal(ReferralStatus.Pending, (await ReadReferralAsync(referralId)).Status);
    }

    // Loads and validates the referral in the given unit of work without saving.
    private static async Task<(Referral Referral, LoyaltyAccount Referrer, LoyaltyAccount Referred, IReadOnlyList<LoyaltyTransaction> Transactions)> PrepareValidationAsync(
        DentalLoyaltyDbContext context,
        LoyaltyAccountTestData data,
        Guid referralId)
    {
        var referral = (await new ReferralRepository(context).GetByIdForTenantAsync(referralId, data.Clinic.Id))!;
        var accounts = new LoyaltyAccountRepository(context);
        var cycles = new LoyaltyCycleRepository(context);
        var referrer = (await accounts.GetByPatientIdForTenantAsync(referral.ReferrerPatientId, data.Clinic.Id))!;
        var referred = (await accounts.GetByPatientIdForTenantAsync(referral.ReferredPatientId, data.Clinic.Id))!;
        var referrerCycle = await cycles.GetActiveForPatientAsync(referral.ReferrerPatientId, data.Clinic.Id);
        var referredCycle = await cycles.GetActiveForPatientAsync(referral.ReferredPatientId, data.Clinic.Id);
        var transactions = referral.Validate(
            new ReferralParticipant(referrer, referrerCycle, referrerCycle?.StartedAt),
            new ReferralParticipant(referred, referredCycle, referredCycle?.StartedAt),
            DateTime.UtcNow,
            data.Admin.Id).Transactions;
        return (referral, referrer, referred, transactions);
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
