using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// Proves the "one User is linked to at most one Patient" invariant is enforced by
// the database itself, not just assumed by the SingleOrDefaultAsync lookup behind
// /api/patients/me. EF Core InMemory does not enforce unique indexes, so these
// tests use an in-memory SQLite database - a real relational engine that honours
// unique filtered (partial) indexes - built from the same entity configurations.
public sealed class PatientUserIdUniquenessTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PatientUserIdUniquenessTests()
    {
        // The in-memory database lives as long as this open connection.
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

    private async Task<(Guid ClinicId, Guid UserId)> SeedClinicWithPatientUserAsync()
    {
        await using var context = CreateContext();

        var clinic = new Clinic("Clinic A");
        var user = new User(clinic.Id, $"patient-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);

        context.Clinics.Add(clinic);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return (clinic.Id, user.Id);
    }

    [Fact]
    public async Task Patient_WithLinkedUserId_CanBePersisted()
    {
        var (clinicId, userId) = await SeedClinicWithPatientUserAsync();

        await using (var context = CreateContext())
        {
            var patient = new Patient(clinicId, "Alice", "Anderson", "+15550100");
            patient.LinkUser(userId);
            context.Patients.Add(patient);
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Patients.CountAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task SecondPatient_WithSameUserId_IsRejectedByDatabase()
    {
        var (clinicId, userId) = await SeedClinicWithPatientUserAsync();

        await using (var context = CreateContext())
        {
            var first = new Patient(clinicId, "Alice", "Anderson", "+15550100");
            first.LinkUser(userId);
            context.Patients.Add(first);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var second = new Patient(clinicId, "Amy", "Adams", "+15550102");
            second.LinkUser(userId);
            context.Patients.Add(second);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
            Assert.Contains("UNIQUE constraint failed: Patients.UserId", sqliteException.Message);
        }

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Patients.CountAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task MultiplePatients_WithoutUserId_AreAllowed()
    {
        var (clinicId, _) = await SeedClinicWithPatientUserAsync();

        await using (var context = CreateContext())
        {
            context.Patients.AddRange(
                new Patient(clinicId, "Alice", "Anderson", "+15550100"),
                new Patient(clinicId, "Amy", "Adams", "+15550102"),
                new Patient(clinicId, "Bob", "Baker", "+15550101"));
            await context.SaveChangesAsync();
        }

        await using var verify = CreateContext();
        Assert.Equal(3, await verify.Patients.CountAsync(p => p.UserId == null));
    }

    [Fact]
    public void SqlServerModel_DefinesUniqueFilteredIndexOnUserId()
    {
        // Renders the production (SQL Server) DDL from the model without opening
        // a connection, pinning the exact index the migration creates.
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlServer("Server=unused;Database=unused")
            .Options;

        using var context = new DentalLoyaltyDbContext(options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains(
            "CREATE UNIQUE INDEX [IX_Patients_UserId] ON [Patients] ([UserId]) WHERE [UserId] IS NOT NULL;",
            script);
    }
}
