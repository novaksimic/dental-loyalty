using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// Persistence of the per-clinic scoring configuration (BUSINESS.md §3.1;
// ADR-003: Clinic 1 ─── 1 LoyaltyConfiguration). Uses an in-memory SQLite
// database - a real relational engine that enforces unique indexes, foreign keys
// and CHECK constraints - built from the same entity configurations.
public sealed class LoyaltyConfigurationPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public LoyaltyConfigurationPersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private DentalLoyaltyDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new DentalLoyaltyDbContext(options);
    }

    private async Task<Guid> AddClinicAsync(string name, LoyaltyConfiguration? configuration = null)
    {
        await using var context = CreateContext();

        var clinic = new Clinic(name);
        configuration ??= LoyaltyConfiguration.CreateDefault(clinic.Id);

        await new ClinicRepository(context).AddAsync(clinic, configuration);

        return clinic.Id;
    }

    [Fact]
    public async Task ClinicRepository_AddAsync_PersistsClinicTogetherWithItsConfiguration()
    {
        var clinicId = await AddClinicAsync("Clinic A");

        await using var verify = CreateContext();
        Assert.True(await verify.Clinics.AnyAsync(c => c.Id == clinicId));

        var configuration = await verify.LoyaltyConfigurations.SingleAsync(lc => lc.ClinicId == clinicId);
        Assert.Equal(100m, configuration.SpendAmountRsd);
        Assert.Equal(1_000, configuration.PointsAwarded);
    }

    [Fact]
    public async Task GetByClinicIdAsync_ReturnsEachClinicsOwnConfiguration()
    {
        var clinicA = new Clinic("Clinic A");
        var clinicB = new Clinic("Clinic B");

        await using (var context = CreateContext())
        {
            var repository = new ClinicRepository(context);
            await repository.AddAsync(clinicA, LoyaltyConfiguration.CreateDefault(clinicA.Id));
            await repository.AddAsync(clinicB, new LoyaltyConfiguration(clinicB.Id, 250.50m, 3_000));
        }

        await using var verify = CreateContext();
        var configurations = new LoyaltyConfigurationRepository(verify);

        var a = await configurations.GetByClinicIdAsync(clinicA.Id);
        var b = await configurations.GetByClinicIdAsync(clinicB.Id);

        Assert.Equal(clinicA.Id, a!.ClinicId);
        Assert.Equal(100m, a.SpendAmountRsd);
        Assert.Equal(1_000, a.PointsAwarded);

        Assert.Equal(clinicB.Id, b!.ClinicId);
        Assert.Equal(250.50m, b.SpendAmountRsd);
        Assert.Equal(3_000, b.PointsAwarded);
    }

    [Fact]
    public async Task GetByClinicIdAsync_ForUnknownClinic_ReturnsNull()
    {
        await AddClinicAsync("Clinic A");

        await using var context = CreateContext();

        Assert.Null(await new LoyaltyConfigurationRepository(context).GetByClinicIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateAsync_PersistsChangedRatio_AndLeavesOtherClinicsUnchanged()
    {
        var clinicAId = await AddClinicAsync("Clinic A");
        var clinicBId = await AddClinicAsync("Clinic B");

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyConfigurationRepository(context);
            var configuration = await repository.GetByClinicIdAsync(clinicAId);
            configuration!.UpdateScoring(50m, 2_000);
            await repository.UpdateAsync(configuration);
        }

        await using var verify = CreateContext();
        var configurations = new LoyaltyConfigurationRepository(verify);

        var a = await configurations.GetByClinicIdAsync(clinicAId);
        Assert.Equal(50m, a!.SpendAmountRsd);
        Assert.Equal(2_000, a.PointsAwarded);
        Assert.NotNull(a.UpdatedAt);

        var b = await configurations.GetByClinicIdAsync(clinicBId);
        Assert.Equal(100m, b!.SpendAmountRsd);
        Assert.Equal(1_000, b.PointsAwarded);
    }

    [Fact]
    public async Task SecondConfiguration_ForSameClinic_IsRejectedByDatabase()
    {
        var clinicId = await AddClinicAsync("Clinic A");

        await using (var context = CreateContext())
        {
            context.LoyaltyConfigurations.Add(new LoyaltyConfiguration(clinicId, 50m, 500));

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
            Assert.Contains("UNIQUE constraint failed: LoyaltyConfigurations.ClinicId", sqliteException.Message);
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.LoyaltyConfigurations.CountAsync(lc => lc.ClinicId == clinicId));
    }

    [Fact]
    public async Task Configuration_ForNonexistentClinic_IsRejectedByDatabase()
    {
        await using var context = CreateContext();
        context.LoyaltyConfigurations.Add(LoyaltyConfiguration.CreateDefault(Guid.NewGuid()));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Contains("FOREIGN KEY constraint failed", sqliteException.Message);
    }

    // The domain already rejects these; the CHECK constraint guards writes that
    // bypass it.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositivePointsAwarded_WrittenDirectly_IsRejectedByDatabase(int pointsAwarded)
    {
        var clinic = new Clinic("Clinic A");

        await using (var context = CreateContext())
        {
            context.Clinics.Add(clinic);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(() =>
                context.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO LoyaltyConfigurations (Id, ClinicId, SpendAmountRsd, PointsAwarded, CreatedAt) VALUES ({Guid.NewGuid()}, {clinic.Id}, {100m}, {pointsAwarded}, {DateTime.UtcNow})"));

            Assert.Contains("CHECK constraint failed: CK_LoyaltyConfigurations_PointsAwarded_Positive", exception.Message);
        }

        await using var verify = CreateContext();
        Assert.False(await verify.LoyaltyConfigurations.AnyAsync(lc => lc.ClinicId == clinic.Id));
    }

    [Fact]
    public void SqlServerModel_DefinesConstraintsForLoyaltyConfigurations()
    {
        // Renders the production (SQL Server) DDL from the model without opening
        // a connection, pinning the constraints the migration creates.
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;

        using var context = new DentalLoyaltyDbContext(options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[SpendAmountRsd] decimal(18,2) NOT NULL", script);
        Assert.Contains("CONSTRAINT [CK_LoyaltyConfigurations_SpendAmountRsd_Positive] CHECK ([SpendAmountRsd] > 0)", script);
        Assert.Contains("CONSTRAINT [CK_LoyaltyConfigurations_PointsAwarded_Positive] CHECK ([PointsAwarded] > 0)", script);
        Assert.Contains("CONSTRAINT [FK_LoyaltyConfigurations_Clinics_ClinicId] FOREIGN KEY ([ClinicId]) REFERENCES [Clinics] ([Id])", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_LoyaltyConfigurations_ClinicId] ON [LoyaltyConfigurations] ([ClinicId]);", script);
    }
}
