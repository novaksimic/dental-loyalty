using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence.Repositories;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// The collation assumption behind "existing logins keep working" (ADR-010 decision 7): on
// SQL Server, with the database built from the real migrations, an existing
// mixed-case email is found with any casing, and the lookup still resolves to
// one user. Skipped unless DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerUserEmailCollationTests
{
    [SqlServerIntegrationFact]
    public async Task MixedCaseAccount_IsFound_WithAnyCasing()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var user = new User(null, "Ana.Admin@Example.com", "not-a-real-hash", UserRole.PlatformAdmin);
        await using (var context = database.CreateContext())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        foreach (var typed in new[] { "Ana.Admin@Example.com", "ana.admin@example.com", "ANA.ADMIN@EXAMPLE.COM" })
        {
            await using var context = database.CreateContext();
            Assert.Equal(user.Id, (await new UserRepository(context).GetByEmailAsync(typed))?.Id);
        }
    }
}
