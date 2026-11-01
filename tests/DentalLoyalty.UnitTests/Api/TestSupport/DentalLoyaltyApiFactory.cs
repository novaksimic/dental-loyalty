using DentalLoyalty.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DentalLoyalty.UnitTests.Api.TestSupport;

// Boots the real API pipeline (Program.cs, DI, authentication/authorization
// middleware) so Slice 4's JWT/role behavior is exercised end to end, while
// swapping the SQL Server DbContext for an isolated in-memory one per factory
// instance and pointing Jwt configuration at TestJwt so tests never depend on
// a real database or the appsettings.json signing key.
public sealed class DentalLoyaltyApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"dental-loyalty-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = TestJwt.Issuer,
                ["Jwt:Audience"] = TestJwt.Audience,
                ["Jwt:SigningKey"] = TestJwt.SigningKey,
                ["Jwt:AccessTokenLifetimeMinutes"] = "60",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<DentalLoyaltyDbContext>>();

            // AddDbContext accumulates configuration via
            // IDbContextOptionsConfiguration<TContext> entries (so multiple
            // AddDbContext calls can contribute to the same context) - removing
            // only DbContextOptions<TContext> above leaves the original
            // UseSqlServer(...) configuration registered alongside the
            // UseInMemoryDatabase(...) one added below, which EF Core rejects
            // as two database providers on one context.
            services.RemoveAll<IDbContextOptionsConfiguration<DentalLoyaltyDbContext>>();

            // The in-memory provider has no transactions; the explicit one that
            // saves a loyalty cycle transition (LoyaltyAccountRepository) is
            // proven on SQLite and SQL Server, so it is ignored here.
            services.AddDbContext<DentalLoyaltyDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName)
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        });
    }
}
