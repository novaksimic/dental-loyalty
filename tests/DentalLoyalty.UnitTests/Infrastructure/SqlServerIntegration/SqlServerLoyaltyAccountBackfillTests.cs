using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DentalLoyalty.UnitTests.Infrastructure.SqlServerIntegration;

// ADR-006 §7: the backfill migration's SQL run against SQL Server itself, on a
// disposable database built from the real migrations. Skipped unless
// DENTALLOYALTY_TEST_SQLSERVER is configured.
public class SqlServerLoyaltyAccountBackfillTests
{
    private const string MigrationBeforeBackfill = "20261018115403_AddLoyaltyCycles";
    private const string BackfillMigration = "20261018115429_BackfillLoyaltyAccountsForExistingPatients";

    private static async Task MigrateToAsync(SqlServerTestDatabase database, string migration)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(migration);
    }

    [SqlServerIntegrationFact]
    public async Task Backfill_CreatesOneZeroBalanceAccountPerPatientWithout_InThePatientsClinic_AndIsIdempotent()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();

        // Back to the state before the backfill, then add Patients the way they
        // existed before ADR-006: without accounts.
        await MigrateToAsync(database, MigrationBeforeBackfill);

        var clinicA = new Clinic("Clinic A");
        var clinicB = new Clinic("Clinic B");
        var withoutAccountA = new Patient(clinicA.Id, "Ana", "Petrovic", "+381600000000");
        var withoutAccountB = new Patient(clinicB.Id, "Bob", "Baker", "+15550101");
        var withAccount = new Patient(clinicA.Id, "Marko", "Jovanovic", "+381600000001");
        var existingAccount = LoyaltyAccount.OpenFor(withAccount);

        await using (var context = database.CreateContext())
        {
            context.Clinics.AddRange(clinicA, clinicB);
            context.Patients.AddRange(withoutAccountA, withoutAccountB, withAccount);
            context.LoyaltyAccounts.Add(existingAccount);
            await context.SaveChangesAsync();
        }

        await MigrateToAsync(database, BackfillMigration);

        // Running it again (down is a no-op, then up again) inserts nothing new.
        await MigrateToAsync(database, MigrationBeforeBackfill);
        await MigrateToAsync(database, BackfillMigration);

        // Read through the current model, which needs the later migrations; they
        // do not touch accounts, patients or cycles.
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await using var verify = database.CreateContext();
        var accounts = await verify.LoyaltyAccounts.AsNoTracking().ToListAsync();
        var patients = await verify.Patients.AsNoTracking().ToListAsync();

        Assert.Equal(3, accounts.Count);
        Assert.All(patients, p => Assert.Single(accounts, a => a.PatientId == p.Id));
        Assert.All(accounts, a => Assert.Equal(patients.Single(p => p.Id == a.PatientId).ClinicId, a.ClinicId));
        Assert.All(accounts, a => Assert.Equal(0, a.Points));
        Assert.All(accounts, a => Assert.Equal(8, a.RowVersion.Length));

        // The account that already existed is untouched.
        Assert.Equal(existingAccount.Id, accounts.Single(a => a.PatientId == withAccount.Id).Id);

        // No Patient data changed; no history and no cycle was created.
        Assert.All(patients, p => Assert.Null(p.UpdatedAt));
        Assert.All(patients, p => Assert.Null(p.UserId));
        Assert.Empty(await verify.LoyaltyTransactions.ToListAsync());
        Assert.Empty(await verify.LoyaltyCycles.ToListAsync());
    }
}
