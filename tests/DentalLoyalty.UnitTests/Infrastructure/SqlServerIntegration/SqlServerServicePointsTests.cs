using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// Recording a service and its points against SQL Server itself, on a disposable
// database built from the real migrations: the real rowversion, the filtered
// unique index on LoyaltyTransactions.PatientServiceId, SQL Server's duplicate-key
// errors and a failed insert rolling back the whole operation. Skipped unless
// DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerServicePointsTests
{
    private static async Task<(int Points, int Transactions, int Services)> ReadStateAsync(SqlServerTestDatabase database, Guid patientId)
    {
        await using var context = database.CreateContext();
        var points = await context.LoyaltyAccounts.Where(a => a.PatientId == patientId).Select(a => a.Points).SingleAsync();
        var transactions = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId);
        var services = await context.PatientServices.CountAsync(ps => ps.PatientId == patientId);
        return (points, transactions, services);
    }

    private static RecordPatientServiceHandler CreateHandler(DentalLoyaltyDbContext context, LoyaltyAccountTestData data)
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

    private static async Task<LoyaltyAccountTestData> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = database.CreateContext();
        return await LoyaltyAccountTestData.SeedAsync(context);
    }

    [SqlServerIntegrationFact]
    public async Task Handler_RecordsServiceWithPoints_AndRepeatedOccurrenceIsNotCreditedAgain()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var data = await SeedAsync(database);
        var command = new RecordPatientServiceCommand(data.PatientA.Id, Guid.NewGuid(), data.Service.Id, null);

        RecordPatientServiceResult first;
        await using (var context = database.CreateContext())
        {
            first = await CreateHandler(context, data).HandleAsync(command);
        }

        RecordPatientServiceResult repeated;
        await using (var context = database.CreateContext())
        {
            repeated = await CreateHandler(context, data).HandleAsync(command);
        }

        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, first.Status);
        Assert.Equal(75_000, first.CreditedPoints);
        Assert.Equal(RecordPatientServiceStatus.AlreadyRecorded, repeated.Status);
        Assert.Equal((75_000, 1, 1), await ReadStateAsync(database, data.PatientA.Id));

        await using var verify = database.CreateContext();
        var transaction = await verify.LoyaltyTransactions.SingleAsync(t => t.PatientId == data.PatientA.Id);
        Assert.Equal(command.PatientServiceId, transaction.PatientServiceId);
    }

    [SqlServerIntegrationFact]
    public async Task SecondTransactionForTheSameOccurrence_IsRejectedByTheUniqueIndex()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var data = await SeedAsync(database);
        var patientService = data.RecordService(data.PatientA);

        await using (var context = database.CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            await repository.SaveServicePointsAsync(patientService, account!, account!.EarnServicePoints(patientService, data.CycleA));
        }

        await using (var context = database.CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var again = account!.EarnServicePoints(patientService, data.CycleA);

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveBalanceChangesAsync([account!], [again]));
            Assert.Contains("IX_LoyaltyTransactions_PatientServiceId", failure.InnerException!.Message);
        }

        Assert.Equal((75_000, 1, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    // At the maximum the service changes no balance, but the account's version is
    // still checked (ADR-008): the stale second attempt conflicts - or, should it
    // get past that, SQL Server's duplicate key on the occurrence stops it.
    [SqlServerIntegrationFact]
    public async Task ConcurrentSubmissionsOfTheSameOccurrence_AtTheMaximum_AreStoppedByTheOccurrenceId()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var data = await SeedAsync(database);

        await using (var context = database.CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var toMaximum = data.RecordService(data.PatientA, 100_000m);
            await repository.SaveServicePointsAsync(toMaximum, account!, account!.EarnServicePoints(toMaximum, data.CycleA));
        }

        var occurrenceId = Guid.NewGuid();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);
        var firstAccount = await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var secondAccount = await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);

        var firstService = new PatientService(occurrenceId, data.PatientA, data.Service, data.Scoring, data.CycleA, DateTime.UtcNow, data.Admin.Id);
        var secondService = new PatientService(occurrenceId, data.PatientA, data.Service, data.Scoring, data.CycleA, DateTime.UtcNow, data.Admin.Id);

        await firstRepository.SaveServicePointsAsync(firstService, firstAccount!, firstAccount!.EarnServicePoints(firstService, data.CycleA));

        var secondAttempt = await Record.ExceptionAsync(() =>
            secondRepository.SaveServicePointsAsync(secondService, secondAccount!, secondAccount!.EarnServicePoints(secondService, data.CycleA)));

        Assert.True(secondAttempt is ConcurrencyConflictException or DuplicateServiceOccurrenceException);
        Assert.Equal((LoyaltyAccount.MaximumPointsBalance, 2, 2), await ReadStateAsync(database, data.PatientA.Id));
    }

    // Two different occurrences racing on one account: the real rowversion
    // conflict rolls back the second operation, its service record included.
    [SqlServerIntegrationFact]
    public async Task ConcurrentDifferentOccurrences_SecondConflicts_AndPersistsNothing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var data = await SeedAsync(database);

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);
        var firstAccount = await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var secondAccount = await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);

        var firstService = data.RecordService(data.PatientA);
        var secondService = data.RecordService(data.PatientA, 3_000m);

        await firstRepository.SaveServicePointsAsync(firstService, firstAccount!, firstAccount!.EarnServicePoints(firstService, data.CycleA));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondRepository.SaveServicePointsAsync(secondService, secondAccount!, secondAccount!.EarnServicePoints(secondService, data.CycleA)));

        Assert.Equal((75_000, 1, 1), await ReadStateAsync(database, data.PatientA.Id));
    }

    [SqlServerIntegrationFact]
    public async Task WhenTheTransactionInsertFails_TheServiceRecordAndBalanceAreRolledBack()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var data = await SeedAsync(database);

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
                CreateHandler(context, data).HandleAsync(
                    new RecordPatientServiceCommand(data.PatientA.Id, Guid.NewGuid(), data.Service.Id, null)));
        }

        Assert.Equal((0, 0, 0), await ReadStateAsync(database, data.PatientA.Id));
    }
}
