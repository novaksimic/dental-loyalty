using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure;

// UserRepository.AddAsync against a relational unique index (SQLite): a user
// whose email is already taken - the race a duplicate check cannot close - is
// reported as DuplicateEmailException and leaves nothing behind (ADR-010).
public sealed class UserRepositoryAddTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public UserRepositoryAddTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private DentalLoyaltyDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DentalLoyaltyDbContext>().UseSqlite(_connection).Options);

    private async Task<Guid> SeedClinicAsync()
    {
        await using var context = CreateContext();
        var clinic = new Clinic("Bright Smiles Dental");
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        return clinic.Id;
    }

    [Fact]
    public async Task AddAsync_PersistsUser()
    {
        var clinicId = await SeedClinicAsync();
        var user = new User(clinicId, "admin@clinic.test", "hash", UserRole.ClinicAdmin);

        await using (var context = CreateContext())
        {
            await new UserRepository(context).AddAsync(user);
        }

        await using var verify = CreateContext();
        var stored = await verify.Users.SingleAsync(u => u.Id == user.Id);

        Assert.Equal(clinicId, stored.ClinicId);
        Assert.Equal(UserRole.ClinicAdmin, stored.Role);
    }

    [Fact]
    public async Task AddAsync_EmailAlreadyTaken_ThrowsDuplicateEmail_AndSavesNothing()
    {
        var clinicId = await SeedClinicAsync();

        await using (var context = CreateContext())
        {
            await new UserRepository(context).AddAsync(new User(clinicId, "admin@clinic.test", "hash", UserRole.ClinicAdmin));
        }

        var second = new User(clinicId, "admin@clinic.test", "other-hash", UserRole.ClinicAdmin);

        await using (var context = CreateContext())
        {
            await Assert.ThrowsAsync<DuplicateEmailException>(() => new UserRepository(context).AddAsync(second));
        }

        await using var verify = CreateContext();

        Assert.Equal(1, await verify.Users.CountAsync(u => u.Email == "admin@clinic.test"));
        Assert.False(await verify.Users.AnyAsync(u => u.Id == second.Id));
    }

    // A save that fails for another reason is not misreported as a duplicate.
    [Fact]
    public async Task AddAsync_OtherFailure_IsNotReportedAsDuplicate()
    {
        var unknownClinic = Guid.NewGuid();

        await using var context = CreateContext();
        var user = new User(unknownClinic, "admin@clinic.test", "hash", UserRole.ClinicAdmin);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => new UserRepository(context).AddAsync(user));

        Assert.IsNotType<DuplicateEmailException>(exception);
    }
}
