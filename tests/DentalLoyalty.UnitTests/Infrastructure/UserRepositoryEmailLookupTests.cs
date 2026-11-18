using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure;

// UserRepository.GetByEmailAsync on SQLite, a case-sensitive relational engine
// (string equality uses the BINARY collation): the email as typed or its
// lower-cased form, exact match preferred (ADR-010 decision 7). SQL Server's
// case-insensitive collation is covered by SqlServerUserEmailCollationTests.
public sealed class UserRepositoryEmailLookupTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public UserRepositoryEmailLookupTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private DentalLoyaltyDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DentalLoyaltyDbContext>().UseSqlite(_connection).Options);

    private async Task<User> SeedAsync(string email)
    {
        await using var context = CreateContext();
        var user = new User(null, email, "not-a-real-hash", UserRole.PlatformAdmin);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private async Task<User?> FindAsync(string email)
    {
        await using var context = CreateContext();
        return await new UserRepository(context).GetByEmailAsync(email);
    }

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ANA@Example.COM")]
    [InlineData("Ana@example.com")]
    public async Task LowerCaseAccount_IsFound_WithAnyCasing(string typed)
    {
        var user = await SeedAsync("ana@example.com");

        Assert.Equal(user.Id, (await FindAsync(typed))?.Id);
    }

    [Fact]
    public async Task MixedCaseAccount_IsFound_WithItsStoredCasing()
    {
        var user = await SeedAsync("Ana.Admin@Example.com");

        Assert.Equal(user.Id, (await FindAsync("Ana.Admin@Example.com"))?.Id);
    }

    // Known compatibility limitation (ADR-010 decision 7).
    [Fact]
    public async Task MixedCaseAccount_IsNotFound_WithADifferentCasing()
    {
        await SeedAsync("Ana.Admin@Example.com");

        Assert.Null(await FindAsync("ana.admin@example.com"));
        Assert.Null(await FindAsync("ANA.ADMIN@EXAMPLE.COM"));
    }

    // Only possible on a case-sensitive store, where the unique index lets both
    // exist: the exact match wins, and other casings resolve to the lower-case
    // account - never an error.
    [Fact]
    public async Task ExactMatch_IsPreferred_OverTheLowerCasedForm()
    {
        var mixed = await SeedAsync("Ana@example.com");
        var lower = await SeedAsync("ana@example.com");

        Assert.Equal(mixed.Id, (await FindAsync("Ana@example.com"))?.Id);
        Assert.Equal(lower.Id, (await FindAsync("ana@example.com"))?.Id);
        Assert.Equal(lower.Id, (await FindAsync("ANA@EXAMPLE.COM"))?.Id);
    }

    [Fact]
    public async Task UnknownEmail_IsNotFound()
    {
        await SeedAsync("ana@example.com");

        Assert.Null(await FindAsync("marko@example.com"));
    }
}
