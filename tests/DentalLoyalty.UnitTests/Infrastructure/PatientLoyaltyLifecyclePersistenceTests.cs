using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// ADR-006 and BUSINESS.md §5.1 on a real relational engine (in-memory SQLite)
// built from the production entity configurations: a Patient is saved together
// with its LoyaltyAccount, and a User, its link and the first LoyaltyCycle are
// saved together - all or nothing. Failures are forced with a trigger that aborts
// the second insert, after the first statement of the same save has run.
public sealed class PatientLoyaltyLifecyclePersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PatientLoyaltyLifecyclePersistenceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private SqliteRowVersionContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new SqliteRowVersionContext(options);
    }

    private async Task<Clinic> SeedClinicAsync(string name = "Clinic A")
    {
        await using var context = CreateContext();
        var clinic = new Clinic(name);
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        return clinic;
    }

    private async Task<Patient> SeedPatientWithAccountAsync(Guid clinicId)
    {
        await using var context = CreateContext();
        var patient = new Patient(clinicId, "Ana", "Petrovic", "+381600000000");
        await new PatientRepository(context).AddAsync(patient, LoyaltyAccount.OpenFor(patient));
        return patient;
    }

    private void FailInsertsInto(string table)
    {
        // table is always a table name written in this test class, never input.
        var sql = $"""
            CREATE TRIGGER TR_Fail_{table} BEFORE INSERT ON {table}
            BEGIN
                SELECT RAISE(ABORT, 'forced failure');
            END;
            """;

        using var context = CreateContext();
        context.Database.ExecuteSqlRaw(sql);
    }

    private static User NewPatientUser(Guid clinicId) =>
        new(clinicId, $"patient-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);

    [Fact]
    public async Task AddAsync_PersistsPatientAndZeroBalanceAccountInTheSameClinic()
    {
        var clinic = await SeedClinicAsync();
        var patient = await SeedPatientWithAccountAsync(clinic.Id);

        await using var verify = CreateContext();
        var account = await verify.LoyaltyAccounts.SingleAsync(a => a.PatientId == patient.Id);

        Assert.True(await verify.Patients.AnyAsync(p => p.Id == patient.Id));
        Assert.Equal(clinic.Id, account.ClinicId);
        Assert.Equal(0, account.Points);
        Assert.Empty(await verify.LoyaltyTransactions.ToListAsync());
        Assert.Empty(await verify.LoyaltyCycles.ToListAsync());
    }

    [Fact]
    public async Task AddAsync_WhenAccountInsertFails_PersistsNoPatient()
    {
        var clinic = await SeedClinicAsync();
        FailInsertsInto("LoyaltyAccounts");

        var patient = new Patient(clinic.Id, "Ana", "Petrovic", "+381600000000");

        await using (var context = CreateContext())
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new PatientRepository(context).AddAsync(patient, LoyaltyAccount.OpenFor(patient)));
        }

        await using var verify = CreateContext();
        Assert.False(await verify.Patients.AnyAsync(p => p.Id == patient.Id));
        Assert.False(await verify.LoyaltyAccounts.AnyAsync(a => a.PatientId == patient.Id));
    }

    [Fact]
    public async Task AddAsync_WithAccountOfAnotherPatientOrClinic_IsRejected_AndPersistsNothing()
    {
        var clinic = await SeedClinicAsync();
        var otherClinic = await SeedClinicAsync("Clinic B");
        var patient = new Patient(clinic.Id, "Ana", "Petrovic", "+381600000000");
        var otherPatient = new Patient(clinic.Id, "Marko", "Jovanovic", "+381600000001");

        await using (var context = CreateContext())
        {
            var repository = new PatientRepository(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.AddAsync(patient, new LoyaltyAccount(otherClinic.Id, patient.Id)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.AddAsync(patient, LoyaltyAccount.OpenFor(otherPatient)));
        }

        await using var verify = CreateContext();
        Assert.Empty(await verify.Patients.ToListAsync());
        Assert.Empty(await verify.LoyaltyAccounts.ToListAsync());
    }

    [Fact]
    public async Task SecondAccountForTheSamePatient_IsRejectedByTheDatabase()
    {
        var clinic = await SeedClinicAsync();
        var patient = await SeedPatientWithAccountAsync(clinic.Id);

        await using var context = CreateContext();
        context.LoyaltyAccounts.Add(new LoyaltyAccount(clinic.Id, patient.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task LinkUserAsync_PersistsUserLinkAndActiveFirstCycle()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);
        var user = NewPatientUser(clinic.Id);

        await using (var context = CreateContext())
        {
            var repository = new PatientRepository(context);
            var patient = await repository.GetByIdForTenantAsync(seeded.Id, clinic.Id);
            patient!.LinkUser(user.Id);

            await repository.LinkUserAsync(patient, user, LoyaltyCycle.StartFirst(patient));
        }

        await using var verify = CreateContext();
        var linked = await verify.Patients.SingleAsync(p => p.Id == seeded.Id);
        var cycle = await verify.LoyaltyCycles.SingleAsync(c => c.PatientId == seeded.Id);

        Assert.Equal(user.Id, linked.UserId);
        Assert.True(await verify.Users.AnyAsync(u => u.Id == user.Id));
        Assert.Equal(clinic.Id, cycle.ClinicId);
        Assert.Equal(LoyaltyCycleStatus.Active, cycle.Status);

        // Starting the cycle grants nothing: the account is still at zero.
        var account = await verify.LoyaltyAccounts.SingleAsync(a => a.PatientId == seeded.Id);
        Assert.Equal(0, account.Points);
        Assert.Empty(await verify.LoyaltyTransactions.ToListAsync());
    }

    [Fact]
    public async Task LinkUserAsync_WhenCycleInsertFails_PersistsNoUserAndNoLink()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);
        var user = NewPatientUser(clinic.Id);
        FailInsertsInto("LoyaltyCycles");

        await using (var context = CreateContext())
        {
            var repository = new PatientRepository(context);
            var patient = await repository.GetByIdForTenantAsync(seeded.Id, clinic.Id);
            patient!.LinkUser(user.Id);

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                repository.LinkUserAsync(patient, user, LoyaltyCycle.StartFirst(patient)));
        }

        await using var verify = CreateContext();
        Assert.Null((await verify.Patients.SingleAsync(p => p.Id == seeded.Id)).UserId);
        Assert.False(await verify.Users.AnyAsync(u => u.Id == user.Id));
        Assert.Empty(await verify.LoyaltyCycles.ToListAsync());
    }

    [Fact]
    public async Task LinkUserAsync_WithUserOrCycleOfAnotherClinic_IsRejected_AndPersistsNothing()
    {
        var clinic = await SeedClinicAsync();
        var otherClinic = await SeedClinicAsync("Clinic B");
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);
        var otherClinicPatient = new Patient(otherClinic.Id, "Bob", "Baker", "+15550101");
        otherClinicPatient.LinkUser(Guid.NewGuid());

        await using (var context = CreateContext())
        {
            var repository = new PatientRepository(context);
            var patient = await repository.GetByIdForTenantAsync(seeded.Id, clinic.Id);

            var foreignUser = NewPatientUser(otherClinic.Id);
            patient!.LinkUser(foreignUser.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.LinkUserAsync(patient, foreignUser, LoyaltyCycle.StartFirst(patient)));
        }

        await using (var context = CreateContext())
        {
            var repository = new PatientRepository(context);
            var patient = await repository.GetByIdForTenantAsync(seeded.Id, clinic.Id);

            var user = NewPatientUser(clinic.Id);
            patient!.LinkUser(user.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.LinkUserAsync(patient, user, LoyaltyCycle.StartFirst(otherClinicPatient)));
        }

        await using var verify = CreateContext();
        Assert.Null((await verify.Patients.SingleAsync(p => p.Id == seeded.Id)).UserId);
        Assert.Empty(await verify.Users.ToListAsync());
        Assert.Empty(await verify.LoyaltyCycles.ToListAsync());
    }

    [Fact]
    public async Task LinkUserAsync_WithPatientNotLoadedThroughTheRepository_IsRejected()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);
        var user = NewPatientUser(clinic.Id);
        seeded.LinkUser(user.Id);

        await using var context = CreateContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PatientRepository(context).LinkUserAsync(seeded, user, LoyaltyCycle.StartFirst(seeded)));
    }

    // The real registration flow - CreatePatientUserHandler over the real
    // repositories, sharing one context as in the API's request scope.
    private async Task<Guid> RegisterAsync(Guid patientId, string email)
    {
        await using var context = CreateContext();
        var handler = new CreatePatientUserHandler(
            new UserRepository(context),
            new PatientRepository(context),
            new PasswordHasher());

        return await handler.HandleAsync(new CreatePatientUserCommand(patientId, email, "Sup3rSecret!"));
    }

    private async Task<(Guid? UserId, DateTime? UpdatedAt, Guid[] UserIds, (Guid Id, DateTime StartedAt)[] Cycles)> ReadLinkStateAsync(Guid patientId)
    {
        await using var context = CreateContext();
        var patient = await context.Patients.AsNoTracking().SingleAsync(p => p.Id == patientId);
        var userIds = await context.Users.Select(u => u.Id).ToArrayAsync();
        var cycles = await context.LoyaltyCycles
            .Where(c => c.PatientId == patientId)
            .Select(c => new { c.Id, c.StartedAt })
            .ToListAsync();

        return (patient.UserId, patient.UpdatedAt, userIds, cycles.Select(c => (c.Id, c.StartedAt)).ToArray());
    }

    [Fact]
    public async Task Register_RepeatedForTheSameUserAfterSuccess_IsRejected_AndChangesNothing()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);

        var userId = await RegisterAsync(seeded.Id, "ana@patient.test");
        var before = await ReadLinkStateAsync(seeded.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RegisterAsync(seeded.Id, "ana@patient.test"));

        AssertLinkUnchanged(before, await ReadLinkStateAsync(seeded.Id), userId);
    }

    private static void AssertLinkUnchanged(
        (Guid? UserId, DateTime? UpdatedAt, Guid[] UserIds, (Guid Id, DateTime StartedAt)[] Cycles) before,
        (Guid? UserId, DateTime? UpdatedAt, Guid[] UserIds, (Guid Id, DateTime StartedAt)[] Cycles) after,
        Guid linkedUserId)
    {
        Assert.Equal(linkedUserId, after.UserId);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(new[] { linkedUserId }, after.UserIds);
        Assert.Equal(before.Cycles, after.Cycles);
        Assert.Single(after.Cycles);
    }

    [Fact]
    public async Task Register_AnotherUserForALinkedPatient_IsRejected_AndChangesNothing()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);

        var userId = await RegisterAsync(seeded.Id, "ana@patient.test");
        var before = await ReadLinkStateAsync(seeded.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RegisterAsync(seeded.Id, "intruder@patient.test"));

        AssertLinkUnchanged(before, await ReadLinkStateAsync(seeded.Id), userId);
    }

    [Fact]
    public async Task LinkUserAsync_PatientAlreadyLinkedWhenLoaded_IsRejected_AndAddsNoUserOrCycle()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);
        var userId = await RegisterAsync(seeded.Id, "ana@patient.test");
        var before = await ReadLinkStateAsync(seeded.Id);

        await using (var context = CreateContext())
        {
            var repository = new PatientRepository(context);
            var patient = await repository.GetByIdForTenantAsync(seeded.Id, clinic.Id);
            var linkedUser = await context.Users.SingleAsync(u => u.Id == userId);

            // Linking the same User again is a no-op in the domain; the
            // repository still refuses to save it as a new link.
            patient!.LinkUser(linkedUser.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.LinkUserAsync(patient, linkedUser, LoyaltyCycle.StartFirst(patient)));
        }

        AssertLinkUnchanged(before, await ReadLinkStateAsync(seeded.Id), userId);
    }

    // Two first links of the same Patient racing: both read it unlinked. The one
    // saved second fails on the one-active-cycle-per-Patient index and is rolled
    // back entirely, so the first link is not overwritten.
    [Fact]
    public async Task LinkUserAsync_ConcurrentFirstLinks_SecondIsRolledBack_AndFirstLinkStays()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);
        var firstUser = NewPatientUser(clinic.Id);
        var secondUser = NewPatientUser(clinic.Id);

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new PatientRepository(first);
        var secondRepository = new PatientRepository(second);

        var firstPatient = await firstRepository.GetByIdForTenantAsync(seeded.Id, clinic.Id);
        var secondPatient = await secondRepository.GetByIdForTenantAsync(seeded.Id, clinic.Id);
        firstPatient!.LinkUser(firstUser.Id);
        secondPatient!.LinkUser(secondUser.Id);

        await firstRepository.LinkUserAsync(firstPatient, firstUser, LoyaltyCycle.StartFirst(firstPatient));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            secondRepository.LinkUserAsync(secondPatient, secondUser, LoyaltyCycle.StartFirst(secondPatient)));

        var after = await ReadLinkStateAsync(seeded.Id);
        Assert.Equal(firstUser.Id, after.UserId);
        Assert.Equal(new[] { firstUser.Id }, after.UserIds);
        Assert.Single(after.Cycles);
    }

    // DATA.md §14: only one active cycle per Patient.
    [Fact]
    public async Task SecondActiveCycleForTheSamePatient_IsRejectedByTheDatabase()
    {
        var clinic = await SeedClinicAsync();
        var seeded = await SeedPatientWithAccountAsync(clinic.Id);
        var user = NewPatientUser(clinic.Id);

        await using (var context = CreateContext())
        {
            var repository = new PatientRepository(context);
            var patient = await repository.GetByIdForTenantAsync(seeded.Id, clinic.Id);
            patient!.LinkUser(user.Id);
            await repository.LinkUserAsync(patient, user, LoyaltyCycle.StartFirst(patient));
        }

        await using var second = CreateContext();
        var linked = await second.Patients.SingleAsync(p => p.Id == seeded.Id);
        second.LoyaltyCycles.Add(LoyaltyCycle.StartFirst(linked));

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    // SQLite has no rowversion; a database default stands in for it so accounts
    // can be inserted (as in LoyaltyAccountRepositoryTests). Concurrency is not
    // exercised here.
    private sealed class SqliteRowVersionContext : DentalLoyaltyDbContext
    {
        public SqliteRowVersionContext(DbContextOptions<DentalLoyaltyDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LoyaltyAccount>()
                .Property(a => a.RowVersion)
                .HasDefaultValueSql("randomblob(8)");
        }
    }
}
