using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Referrals.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// Referral validation against SQL Server itself, on a disposable database built
// from the real migrations: the real rowversions on LoyaltyAccounts and
// Referrals, the unique indexes on Referrals (ReferredPatientId) and
// LoyaltyTransactions (ReferralId, PatientId), and a failed insert rolling back
// the whole validation. Skipped unless DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerReferralValidationTests
{
    private static async Task<(int Points, int Referrals, int Transactions)> ReadStateAsync(SqlServerTestDatabase database, Guid patientId)
    {
        await using var context = database.CreateContext();
        var account = await context.LoyaltyAccounts.AsNoTracking().SingleAsync(a => a.PatientId == patientId);
        var transactions = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId);
        return (account.Points, account.ReferralBalance, transactions);
    }

    private static ValidateReferralHandler CreateHandler(DentalLoyaltyDbContext context, LoyaltyAccountTestData data)
    {
        var accounts = new LoyaltyAccountRepository(context);
        return new(
            new ReferralRepository(context),
            new PatientRepository(context),
            accounts,
            new LoyaltyCycleRepository(context),
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            FakeCurrentTenant.ForClinic(data.Clinic.Id),
            FakeCurrentUser.For(data.Admin.Id));
    }

    // The shared test data and a Pending referral from patient A to patient B.
    private static async Task<(LoyaltyAccountTestData Data, Guid ReferralId)> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        var data = await LoyaltyAccountTestData.SeedAsync(context);
        var referral = Referral.Record(data.PatientA, data.PatientB, data.Admin.Id);
        context.Referrals.Add(referral);
        await context.SaveChangesAsync();
        return (data, referral.Id);
    }

    private static async Task<(Referral Referral, LoyaltyAccount Referrer, LoyaltyAccount Referred, IReadOnlyList<LoyaltyTransaction> Transactions)> PrepareValidationAsync(
        DentalLoyaltyDbContext context,
        LoyaltyAccountTestData data,
        Guid referralId)
    {
        var referral = (await new ReferralRepository(context).GetByIdForTenantAsync(referralId, data.Clinic.Id))!;
        var accounts = new LoyaltyAccountRepository(context);
        var referrer = (await accounts.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id))!;
        var referred = (await accounts.GetByPatientIdForTenantAsync(data.PatientB.Id, data.Clinic.Id))!;
        var transactions = referral.Validate(
            new ReferralParticipant(referrer, data.CycleA, data.CycleA.StartedAt),
            new ReferralParticipant(referred, data.CycleB, data.CycleB.StartedAt),
            DateTime.UtcNow,
            data.Admin.Id).Transactions;
        return (referral, referrer, referred, transactions);
    }

    [SqlServerIntegrationFact]
    public async Task Handler_RewardsBoth_AndRepeatedValidationIsRejected()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, referralId) = await SeedAsync(database);

        ValidateReferralResult first;
        await using (var context = database.CreateContext())
        {
            first = await CreateHandler(context, data).HandleAsync(new ValidateReferralCommand(referralId, null));
        }

        ValidateReferralResult repeated;
        await using (var context = database.CreateContext())
        {
            repeated = await CreateHandler(context, data).HandleAsync(new ValidateReferralCommand(referralId, null));
        }

        Assert.Equal(ValidateReferralStatus.Validated, first.Status);
        Assert.Equal(ValidateReferralStatus.NotPending, repeated.Status);
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(database, data.PatientA.Id));
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(database, data.PatientB.Id));
    }

    [SqlServerIntegrationFact]
    public async Task ConcurrentValidations_SecondConflicts_AndPersistsNothing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, referralId) = await SeedAsync(database);

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstValidation = await PrepareValidationAsync(first, data, referralId);
        var secondValidation = await PrepareValidationAsync(second, data, referralId);

        await new LoyaltyAccountRepository(first).SaveReferralValidationAsync(
            firstValidation.Referral, firstValidation.Referrer, firstValidation.Referred, firstValidation.Transactions);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new LoyaltyAccountRepository(second).SaveReferralValidationAsync(
                secondValidation.Referral, secondValidation.Referrer, secondValidation.Referred, secondValidation.Transactions));

        Assert.Equal((50_000, 1, 1), await ReadStateAsync(database, data.PatientA.Id));
        Assert.Equal((50_000, 1, 1), await ReadStateAsync(database, data.PatientB.Id));
    }

    [SqlServerIntegrationFact]
    public async Task SecondReferralOfTheSamePatient_IsRejectedByTheUniqueIndex()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, _) = await SeedAsync(database);

        await using var context = database.CreateContext();
        var failure = await Assert.ThrowsAsync<DuplicateReferralException>(() =>
            new ReferralRepository(context).AddAsync(Referral.Record(data.PatientA, data.PatientB, data.Admin.Id)));
        Assert.Contains("IX_Referrals_ReferredPatientId", failure.InnerException!.InnerException!.Message);
    }

    [SqlServerIntegrationFact]
    public async Task WhenTheTransactionInsertFails_TheValidationIsRolledBack()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (data, referralId) = await SeedAsync(database);

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
                CreateHandler(context, data).HandleAsync(new ValidateReferralCommand(referralId, null)));
        }

        Assert.Equal((0, 0, 0), await ReadStateAsync(database, data.PatientA.Id));
        Assert.Equal((0, 0, 0), await ReadStateAsync(database, data.PatientB.Id));
        await using var readContext = database.CreateContext();
        Assert.Equal(
            ReferralStatus.Pending,
            (await readContext.Referrals.AsNoTracking().SingleAsync(r => r.Id == referralId)).Status);
    }
}
