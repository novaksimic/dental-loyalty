using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// LoyaltyAccountRepository on a real relational engine (in-memory SQLite) built
// from the production entity configurations: tenant-scoped lookup, atomic save of
// account changes with their transaction records, and the append-only guard.
//
// SQLite has no rowversion, so this test context emulates it (a database default
// plus an update trigger). ADR-005: that emulation only supplements - actual SQL
// Server rowversion behaviour is proven by SqlServerLoyaltyAccountConcurrencyTests.
public sealed class LoyaltyAccountRepositoryTests : IDisposable
{
    private const string RowVersionTrigger = "TR_LoyaltyAccounts_RowVersion";

    private readonly SqliteConnection _connection;

    public LoyaltyAccountRepositoryTests()
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

    private async Task<LoyaltyAccountTestData> SeedAsync()
    {
        await using var context = CreateContext();
        return await LoyaltyAccountTestData.SeedAsync(context);
    }

    private async Task<(int Points, int TransactionCount)> ReadStateAsync(Guid patientId)
    {
        await using var context = CreateContext();
        var points = await context.LoyaltyAccounts.Where(a => a.PatientId == patientId).Select(a => a.Points).SingleAsync();
        var transactions = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId);
        return (points, transactions);
    }

    [Fact]
    public async Task GetByPatientIdForTenantAsync_ReturnsAccountOnlyForItsOwnClinic()
    {
        var data = await SeedAsync();

        await using var context = CreateContext();
        var repository = new LoyaltyAccountRepository(context);

        var own = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var otherClinic = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, Guid.NewGuid());

        Assert.NotNull(own);
        Assert.Equal(data.PatientA.Id, own!.PatientId);
        Assert.Null(otherClinic);
    }

    [Fact]
    public async Task SaveBalanceChangesAsync_PersistsAccountChangeAndTransactionTogether()
    {
        var data = await SeedAsync();

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var transaction = data.Earn(context, account!, data.PatientA);

            await repository.SaveBalanceChangesAsync([account!], [transaction]);
        }

        await using var verify = CreateContext();
        var account2 = await verify.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id);
        var stored = await verify.LoyaltyTransactions.SingleAsync(t => t.PatientId == data.PatientA.Id);

        Assert.Equal(75_000, account2.Points);
        Assert.Equal(75_000, stored.PointsChange);
        Assert.Equal(data.Service.Id, stored.ServiceId);
        Assert.Equal(data.Clinic.Id, stored.ClinicId);
        Assert.Equal(data.Admin.Id, stored.CreatedBy);
    }

    [Fact]
    public async Task SaveBalanceChangesAsync_WhenTransactionInsertFails_PersistsNothing()
    {
        var data = await SeedAsync();

        // A catalog service that was never saved: the occurrence's ServiceId
        // violates its foreign key, so its INSERT fails after the account UPDATE
        // was issued.
        var unsavedService = new Service(data.Clinic.Id, "Not in catalog", 1_000m);

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var patientService = new PatientService(
                Guid.NewGuid(), data.PatientA, unsavedService, data.Scoring, data.CycleA, DateTime.UtcNow, data.Admin.Id);
            context.PatientServices.Add(patientService);
            var transaction = account!.EarnServicePoints(patientService, data.CycleA);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                repository.SaveBalanceChangesAsync([account!], [transaction]));
        }

        Assert.Equal((0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    // Supplementary (emulated rowversion). Proven against SQL Server in
    // SqlServerLoyaltyAccountConcurrencyTests.
    [Fact]
    public async Task SaveBalanceChangesAsync_StaleAccount_ThrowsConcurrencyConflict_AndPersistsNothingFromIt()
    {
        var data = await SeedAsync();

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);

        var firstAccount = await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var secondAccount = await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);

        var firstTransaction = data.Earn(first, firstAccount!, data.PatientA);
        var secondTransaction = data.Earn(second, secondAccount!, data.PatientA, 3_000m);

        await firstRepository.SaveBalanceChangesAsync([firstAccount!], [firstTransaction]);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondRepository.SaveBalanceChangesAsync([secondAccount!], [secondTransaction]));

        Assert.Equal((75_000, 1), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task SaveBalanceChangesAsync_TwoAccounts_ConflictOnOne_PersistsNeither()
    {
        var data = await SeedAsync();

        await using var operation = CreateContext();
        var repository = new LoyaltyAccountRepository(operation);
        var accountA = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var accountB = await repository.GetByPatientIdForTenantAsync(data.PatientB.Id, data.Clinic.Id);

        // Another operation changes account B after this operation loaded it.
        await using (var concurrent = CreateContext())
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

        Assert.Equal((0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((10_000, 1), await ReadStateAsync(data.PatientB.Id));
    }

    // ADR-005 §2: a 0-point result is recorded as history without updating the
    // account, so no artificial version change is made.
    [Fact]
    public async Task SaveBalanceChangesAsync_ZeroPointResult_PersistsTransactionWithoutUpdatingAccount()
    {
        var data = await SeedAsync();

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var toMaximum = data.Earn(context, account!, data.PatientA, 100_000m);
            await repository.SaveBalanceChangesAsync([account!], [toMaximum]);
        }

        byte[] versionBefore;
        await using (var context = CreateContext())
        {
            versionBefore = await context.LoyaltyAccounts.Where(a => a.PatientId == data.PatientA.Id).Select(a => a.RowVersion).SingleAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var zero = data.Earn(context, account!, data.PatientA);

            Assert.Equal(EntityState.Unchanged, context.Entry(account!).State);

            await repository.SaveBalanceChangesAsync([account!], [zero]);
        }

        await using var verify = CreateContext();
        var versionAfter = await verify.LoyaltyAccounts.Where(a => a.PatientId == data.PatientA.Id).Select(a => a.RowVersion).SingleAsync();
        var history = await verify.LoyaltyTransactions.Where(t => t.PatientId == data.PatientA.Id).Select(t => t.PointsChange).ToListAsync();

        Assert.Equal(versionBefore, versionAfter);
        Assert.Equal(new[] { 0, 1_000_000 }, history.Order());
    }

    [Fact]
    public async Task SaveBalanceChangesAsync_TransactionForAnotherAccount_IsRejectedAndNothingSaved()
    {
        var data = await SeedAsync();

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var accountA = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var accountB = await repository.GetByPatientIdForTenantAsync(data.PatientB.Id, data.Clinic.Id);
            data.Earn(context, accountA!, data.PatientA);
            var transactionB = data.Earn(context, accountB!, data.PatientB);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SaveBalanceChangesAsync([accountA!], [transactionB]));
        }

        Assert.Equal((0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((0, 0), await ReadStateAsync(data.PatientB.Id));
    }

    [Fact]
    public async Task SaveBalanceChangesAsync_AccountNotLoadedThroughRepository_IsRejected()
    {
        var data = await SeedAsync();

        await using var context = CreateContext();
        var repository = new LoyaltyAccountRepository(context);
        var detached = new LoyaltyAccount(data.Clinic.Id, data.PatientA.Id);
        var transaction = data.Earn(context, detached, data.PatientA);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SaveBalanceChangesAsync([detached], [transaction]));

        Assert.Equal((0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task SaveBalanceChangesAsync_WithChangedExistingTransaction_IsRejected(EntityState change)
    {
        var data = await SeedAsync();

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var transaction = data.Earn(context, account!, data.PatientA);
            await repository.SaveBalanceChangesAsync([account!], [transaction]);
        }

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var existing = await context.LoyaltyTransactions.SingleAsync(t => t.PatientId == data.PatientA.Id);
            context.Entry(existing).State = change;

            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var transaction = data.Earn(context, account!, data.PatientA, 1_000m);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.SaveBalanceChangesAsync([account!], [transaction]));
        }

        Assert.Equal((75_000, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // Test-only model change: SQLite cannot generate rowversion values, so the
    // column gets a random default and the trigger above replaces it on update.
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
