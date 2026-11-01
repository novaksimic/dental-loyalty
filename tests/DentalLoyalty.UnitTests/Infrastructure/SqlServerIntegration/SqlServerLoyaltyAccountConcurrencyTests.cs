using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// ADR-005 Testing: actual SQL Server rowversion behaviour, with two independent
// contexts, on a disposable database built from the real migrations. Skipped
// unless DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerLoyaltyAccountConcurrencyTests
{
    private static async Task<(int Points, int[] History)> ReadStateAsync(SqlServerTestDatabase database, Guid patientId)
    {
        await using var context = database.CreateContext();
        var points = await context.LoyaltyAccounts.Where(a => a.PatientId == patientId).Select(a => a.Points).SingleAsync();
        var history = await context.LoyaltyTransactions.Where(t => t.PatientId == patientId).Select(t => t.PointsChange).ToArrayAsync();
        return (points, history);
    }

    [SqlServerIntegrationFact]
    public async Task RowVersion_IsGeneratedByTheDatabase_AndChangesOnUpdate()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();

        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
        }

        byte[] before;
        await using (var context = database.CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            before = account!.RowVersion;

            var transaction = data.Earn(context, account!, data.PatientA);
            await repository.SaveBalanceChangesAsync([account!], [transaction]);
        }

        await using var verify = database.CreateContext();
        var after = await verify.LoyaltyAccounts.Where(a => a.PatientId == data.PatientA.Id).Select(a => a.RowVersion).SingleAsync();

        Assert.Equal(8, before.Length);
        Assert.NotEqual(before, after);
    }

    [SqlServerIntegrationFact]
    public async Task TwoContexts_UpdateSameAccount_SecondSaveConflicts_AndHistoryMatchesBalance()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();

        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
        }

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);

        var firstAccount = await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var secondAccount = await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);

        var firstTransaction = data.Earn(first, firstAccount!, data.PatientA);
        var secondTransaction = data.Earn(second, secondAccount!, data.PatientA, 3_000m);

        await firstRepository.SaveBalanceChangesAsync([firstAccount!], [firstTransaction]);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondRepository.SaveBalanceChangesAsync([secondAccount!], [secondTransaction]));

        var (points, history) = await ReadStateAsync(database, data.PatientA.Id);
        Assert.Equal(75_000, points);
        Assert.Equal(new[] { 75_000 }, history);
        Assert.Equal(points, history.Sum());
    }

    [SqlServerIntegrationFact]
    public async Task TwoAccountOperation_ConflictOnOneAccount_PersistsNothingFromTheOperation()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();

        LoyaltyAccountTestData data;
        await using (var context = database.CreateContext())
        {
            data = await LoyaltyAccountTestData.SeedAsync(context);
        }

        await using var operation = database.CreateContext();
        var repository = new LoyaltyAccountRepository(operation);
        var accountA = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var accountB = await repository.GetByPatientIdForTenantAsync(data.PatientB.Id, data.Clinic.Id);

        await using (var concurrent = database.CreateContext())
        {
            var concurrentRepository = new LoyaltyAccountRepository(concurrent);
            var concurrentB = await concurrentRepository.GetByPatientIdForTenantAsync(data.PatientB.Id, data.Clinic.Id);
            var concurrentTransaction = data.Earn(concurrent, concurrentB!, data.PatientB, 1_000m);
            await concurrentRepository.SaveBalanceChangesAsync([concurrentB!], [concurrentTransaction]);
        }

        var transactionA = data.Earn(operation, accountA!, data.PatientA);
        var transactionB = data.Earn(operation, accountB!, data.PatientB);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            repository.SaveBalanceChangesAsync([accountA!, accountB!], [transactionA, transactionB]));

        var (pointsA, historyA) = await ReadStateAsync(database, data.PatientA.Id);
        Assert.Equal(0, pointsA);
        Assert.Empty(historyA);

        var (pointsB, historyB) = await ReadStateAsync(database, data.PatientB.Id);
        Assert.Equal(10_000, pointsB);
        Assert.Equal(new[] { 10_000 }, historyB);
    }
}
