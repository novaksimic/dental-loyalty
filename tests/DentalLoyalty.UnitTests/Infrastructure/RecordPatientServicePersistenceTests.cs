using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// Recording a completed service and the points it earns (BUSINESS.md §3, §5.1,
// §14), through RecordPatientServiceHandler and the real repositories on a real
// relational engine (in-memory SQLite) built from the production configurations.
// Every repository shares one context per operation, as in an API request.
//
// SQLite has no rowversion, so this context emulates it (a database default plus
// an update trigger), as LoyaltyAccountRepositoryTests does; SQL Server behaviour
// is proven by SqlServerServicePointsTests.
public sealed class RecordPatientServicePersistenceTests : IDisposable
{
    private const string RowVersionTrigger = "TR_LoyaltyAccounts_RowVersion";

    private readonly SqliteConnection _connection;

    public RecordPatientServicePersistenceTests()
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

    private static RecordPatientServiceHandler CreateHandler(DentalLoyaltyDbContext context, Guid? clinicId, Guid? userId)
    {
        var accounts = new LoyaltyAccountRepository(context);
        return new(
            new PatientRepository(context),
            new ServiceRepository(context),
            new LoyaltyConfigurationRepository(context),
            accounts,
            LoyaltyCycleProgressionTestFactory.Create(context, accounts),
            new PatientServiceRepository(context),
            clinicId is null ? FakeCurrentTenant.NoTenant() : FakeCurrentTenant.ForClinic(clinicId.Value),
            userId is null ? FakeCurrentUser.NoUser() : FakeCurrentUser.For(userId.Value));
    }

    // One API request: a fresh context, the admin of data's clinic as caller.
    private async Task<RecordPatientServiceResult> RecordAsync(
        LoyaltyAccountTestData data,
        Guid patientId,
        Guid serviceId,
        Guid? occurrenceId = null,
        DateTime? performedAt = null,
        Guid? clinicId = null)
    {
        await using var context = CreateContext();
        return await CreateHandler(context, clinicId ?? data.Clinic.Id, data.Admin.Id)
            .HandleAsync(new RecordPatientServiceCommand(patientId, occurrenceId ?? Guid.NewGuid(), serviceId, performedAt));
    }

    private async Task<(int Points, int Transactions, int Services)> ReadStateAsync(Guid patientId)
    {
        await using var context = CreateContext();
        var points = await context.LoyaltyAccounts.Where(a => a.PatientId == patientId).Select(a => a.Points).SingleAsync();
        var transactions = await context.LoyaltyTransactions.CountAsync(t => t.PatientId == patientId);
        var services = await context.PatientServices.CountAsync(ps => ps.PatientId == patientId);
        return (points, transactions, services);
    }

    private async Task<Service> SeedServiceAsync(Guid clinicId, decimal priceRsd, bool active = true)
    {
        await using var context = CreateContext();
        var service = new Service(clinicId, $"Usluga {priceRsd}", priceRsd);

        if (!active)
        {
            service.Deactivate();
        }

        context.Services.Add(service);
        await context.SaveChangesAsync();
        return service;
    }

    // A patient of the clinic with an account and, when linked, a first cycle
    // that started at cycleStartedAt (now by default).
    private async Task<Patient> SeedPatientAsync(Guid clinicId, bool linked, DateTime? cycleStartedAt = null, bool withCycle = true)
    {
        await using var context = CreateContext();
        var patient = new Patient(clinicId, "Pera", "Peric", "+381600000009");
        context.Patients.Add(patient);
        context.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));

        if (linked)
        {
            var user = new User(clinicId, $"pera-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);
            context.Users.Add(user);
            patient.LinkUser(user.Id);

            if (withCycle)
            {
                var cycle = LoyaltyCycle.StartFirst(patient);
                context.LoyaltyCycles.Add(cycleStartedAt is null ? cycle : cycle.StartedOn(cycleStartedAt.Value));
            }
        }

        await context.SaveChangesAsync();
        return patient;
    }

    private async Task<(Clinic Clinic, Patient Patient, Service Service)> SeedOtherClinicAsync()
    {
        await using var context = CreateContext();
        var clinic = new Clinic("Clinic B");
        var service = new Service(clinic.Id, "Usluga B", 7_500m);
        var user = new User(clinic.Id, $"bob-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);
        var patient = new Patient(clinic.Id, "Bob", "Baker", "+15550101");
        patient.LinkUser(user.Id);

        context.Clinics.Add(clinic);
        context.LoyaltyConfigurations.Add(LoyaltyConfiguration.CreateDefault(clinic.Id));
        context.Services.Add(service);
        context.Users.Add(user);
        context.Patients.Add(patient);
        context.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));
        context.LoyaltyCycles.Add(LoyaltyCycle.StartFirst(patient));
        await context.SaveChangesAsync();

        return (clinic, patient, service);
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

    // --- successful accrual ---------------------------------------------------

    [Fact]
    public async Task EligibleService_IsRecorded_CreditsPoints_AndWritesLinkedTransaction()
    {
        var data = await SeedAsync();
        var occurrenceId = Guid.NewGuid();

        var result = await RecordAsync(data, data.PatientA.Id, data.Service.Id, occurrenceId);

        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, result.Status);
        Assert.Equal(occurrenceId, result.PatientServiceId);
        Assert.Equal(75_000, result.EarnedPoints);
        Assert.Equal(75_000, result.CreditedPoints);

        await using var verify = CreateContext();
        var account = await verify.LoyaltyAccounts.SingleAsync(a => a.PatientId == data.PatientA.Id);
        var patientService = await verify.PatientServices.SingleAsync(ps => ps.Id == occurrenceId);
        var transaction = await verify.LoyaltyTransactions.SingleAsync(t => t.PatientId == data.PatientA.Id);

        Assert.Equal(75_000, account.Points);
        Assert.Equal(7_500m, patientService.PriceAtTimeOfService);
        Assert.Equal(75_000, patientService.EarnedPoints);
        Assert.Equal(data.Admin.Id, patientService.RecordedBy);
        Assert.Equal(LoyaltyTransactionType.ServicePointsEarned, transaction.Type);
        Assert.Equal(75_000, transaction.PointsChange);
        Assert.Equal(occurrenceId, transaction.PatientServiceId);
        Assert.Equal(data.Service.Id, transaction.ServiceId);
        Assert.Equal(data.Clinic.Id, transaction.ClinicId);
        Assert.Equal(data.Admin.Id, transaction.CreatedBy);
    }

    // BUSINESS.md §3.1: the clinic's own configured conversion, never a default.
    [Fact]
    public async Task EligibleService_UsesTheTenantsConfiguredConversion()
    {
        var data = await SeedAsync();

        await using (var context = CreateContext())
        {
            var scoring = await context.LoyaltyConfigurations.SingleAsync(lc => lc.ClinicId == data.Clinic.Id);
            scoring.UpdateScoring(100m, 500);
            await context.SaveChangesAsync();
        }

        var result = await RecordAsync(data, data.PatientA.Id, data.Service.Id);

        Assert.Equal(37_500, result.CreditedPoints);
        Assert.Equal((37_500, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // BUSINESS.md §3.2: 1,234.56 RSD -> 12,345.6 -> 12,346 points.
    [Fact]
    public async Task EligibleService_RoundsMidpointAwayFromZero()
    {
        var data = await SeedAsync();
        var service = await SeedServiceAsync(data.Clinic.Id, 1_234.56m);

        var result = await RecordAsync(data, data.PatientA.Id, service.Id);

        Assert.Equal(12_346, result.EarnedPoints);
        Assert.Equal(12_346, result.CreditedPoints);
    }

    // BUSINESS.md §3.3: only what fits is credited, the excess is discarded, and
    // the operation is not rejected - also when nothing fits any more.
    [Fact]
    public async Task EligibleService_AboveTheMaximum_CreditsOnlyWhatFits_ThenNothing()
    {
        var data = await SeedAsync();
        var nearMaximum = await SeedServiceAsync(data.Clinic.Id, 99_000m);

        await RecordAsync(data, data.PatientA.Id, nearMaximum.Id);
        var crossing = await RecordAsync(data, data.PatientA.Id, data.Service.Id);
        var atMaximum = await RecordAsync(data, data.PatientA.Id, data.Service.Id);

        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, crossing.Status);
        Assert.Equal(75_000, crossing.EarnedPoints);
        Assert.Equal(10_000, crossing.CreditedPoints);
        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, atMaximum.Status);
        Assert.Equal(0, atMaximum.CreditedPoints);

        await using var verify = CreateContext();
        var history = await verify.LoyaltyTransactions
            .Where(t => t.PatientId == data.PatientA.Id)
            .Select(t => t.PointsChange)
            .ToListAsync();

        Assert.Equal(LoyaltyAccount.MaximumPointsBalance, (await ReadStateAsync(data.PatientA.Id)).Points);
        Assert.Equal(new[] { 0, 10_000, 990_000 }, history.Order());
    }

    // Different occurrences of the same catalog service each earn points.
    [Fact]
    public async Task TwoOccurrencesOfTheSameCatalogService_EachEarnPoints()
    {
        var data = await SeedAsync();

        await RecordAsync(data, data.PatientA.Id, data.Service.Id);
        await RecordAsync(data, data.PatientA.Id, data.Service.Id);

        Assert.Equal((150_000, 2, 2), await ReadStateAsync(data.PatientA.Id));
    }

    // --- eligibility -------------------------------------------------------------

    // BUSINESS.md §3.1, §5.1: no benefits before registration and linking; the
    // service is still recorded for clinic history.
    [Fact]
    public async Task UnlinkedPatient_ServiceIsRecordedWithoutPointsOrTransaction()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(data.Clinic.Id, linked: false);

        var result = await RecordAsync(data, patient.Id, data.Service.Id);

        Assert.Equal(RecordPatientServiceStatus.RecordedWithoutPoints, result.Status);
        Assert.Equal(0, result.EarnedPoints);
        Assert.Equal((0, 0, 1), await ReadStateAsync(patient.Id));

        await using var verify = CreateContext();
        Assert.Equal(0, (await verify.PatientServices.SingleAsync(ps => ps.PatientId == patient.Id)).EarnedPoints);
    }

    [Fact]
    public async Task LinkedPatientWithoutActiveCycle_ServiceIsRecordedWithoutPoints()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(data.Clinic.Id, linked: true, withCycle: false);

        var result = await RecordAsync(data, patient.Id, data.Service.Id);

        Assert.Equal(RecordPatientServiceStatus.RecordedWithoutPoints, result.Status);
        Assert.Equal((0, 0, 1), await ReadStateAsync(patient.Id));
    }

    // BUSINESS.md §5.1: a service performed before registration and linking is
    // never credited retroactively, although it is recorded afterwards.
    [Fact]
    public async Task ServicePerformedBeforeTheCycleStarted_IsRecordedWithoutPoints()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(data.Clinic.Id, linked: true, cycleStartedAt: DateTime.UtcNow.AddDays(-10));

        var result = await RecordAsync(data, patient.Id, data.Service.Id, performedAt: DateTime.UtcNow.AddDays(-11));

        Assert.Equal(RecordPatientServiceStatus.RecordedWithoutPoints, result.Status);
        Assert.Equal((0, 0, 1), await ReadStateAsync(patient.Id));
    }

    [Fact]
    public async Task ServicePerformedEarlierWithinTheCycle_EarnsPoints()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(data.Clinic.Id, linked: true, cycleStartedAt: DateTime.UtcNow.AddDays(-10));

        var result = await RecordAsync(data, patient.Id, data.Service.Id, performedAt: DateTime.UtcNow.AddDays(-9));

        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, result.Status);
        Assert.Equal((75_000, 1, 1), await ReadStateAsync(patient.Id));
    }

    // BUSINESS.md §5.3, §5.4; ADR-008: the expired cycle is ended and the next
    // started at its one-year end. A service performed in the expired cycle but
    // recorded afterwards is late: recorded for history, without points.
    [Fact]
    public async Task ServiceFromAnExpiredCycle_IsRecordedWithoutPoints_AfterTheCycleRollsOver()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(
            data.Clinic.Id, linked: true, cycleStartedAt: DateTime.UtcNow.AddYears(-1).AddMinutes(-1));

        var result = await RecordAsync(data, patient.Id, data.Service.Id, performedAt: DateTime.UtcNow.AddMonths(-6));

        Assert.Equal(RecordPatientServiceStatus.RecordedWithoutPoints, result.Status);
        // The CycleReset transaction of the expired cycle, and the service.
        Assert.Equal((0, 1, 1), await ReadStateAsync(patient.Id));
    }

    // A service performed after the expiry belongs to the next cycle and earns.
    [Fact]
    public async Task ServiceAfterTheExpiry_EarnsPointsInTheNextCycle()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(
            data.Clinic.Id, linked: true, cycleStartedAt: DateTime.UtcNow.AddYears(-1).AddMinutes(-1));

        var result = await RecordAsync(data, patient.Id, data.Service.Id, performedAt: DateTime.UtcNow);

        Assert.Equal(RecordPatientServiceStatus.RecordedWithPoints, result.Status);
        Assert.Equal((75_000, 2, 1), await ReadStateAsync(patient.Id));
    }

    [Fact]
    public async Task InactivePatient_IsRejected_AndNothingIsRecorded()
    {
        var data = await SeedAsync();

        await using (var context = CreateContext())
        {
            (await context.Patients.SingleAsync(p => p.Id == data.PatientA.Id)).Deactivate();
            await context.SaveChangesAsync();
        }

        var result = await RecordAsync(data, data.PatientA.Id, data.Service.Id);

        Assert.Equal(RecordPatientServiceStatus.PatientInactive, result.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task InactiveCatalogService_IsRejected_AndNothingIsRecorded()
    {
        var data = await SeedAsync();
        var inactive = await SeedServiceAsync(data.Clinic.Id, 2_000m, active: false);

        var result = await RecordAsync(data, data.PatientA.Id, inactive.Id);

        Assert.Equal(RecordPatientServiceStatus.ServiceInactive, result.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    // --- tenant isolation and request validation ----------------------------------

    [Fact]
    public async Task PatientOfAnotherClinic_IsNotFound_AndNothingIsRecorded()
    {
        var data = await SeedAsync();
        var other = await SeedOtherClinicAsync();

        var withOwnService = await RecordAsync(data, other.Patient.Id, data.Service.Id);
        var withTheirService = await RecordAsync(data, other.Patient.Id, other.Service.Id);

        Assert.Equal(RecordPatientServiceStatus.PatientNotFound, withOwnService.Status);
        Assert.Equal(RecordPatientServiceStatus.PatientNotFound, withTheirService.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(other.Patient.Id));
    }

    [Fact]
    public async Task ServiceOfAnotherClinic_IsNotFound_AndNothingIsRecorded()
    {
        var data = await SeedAsync();
        var other = await SeedOtherClinicAsync();

        var result = await RecordAsync(data, data.PatientA.Id, other.Service.Id);

        Assert.Equal(RecordPatientServiceStatus.ServiceNotFound, result.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    // The clinic is always the caller's tenant: another clinic's admin reaches
    // neither this clinic's patient nor its catalog.
    [Fact]
    public async Task CallerFromAnotherClinic_CannotRecordForThisClinicsPatient()
    {
        var data = await SeedAsync();
        var other = await SeedOtherClinicAsync();

        var result = await RecordAsync(data, data.PatientA.Id, data.Service.Id, clinicId: other.Clinic.Id);

        Assert.Equal(RecordPatientServiceStatus.PatientNotFound, result.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task WithoutTenantOrUser_FailsClosed_AndNothingIsRecorded()
    {
        var data = await SeedAsync();
        var command = new RecordPatientServiceCommand(data.PatientA.Id, Guid.NewGuid(), data.Service.Id, null);

        await using (var context = CreateContext())
        {
            Assert.Equal(
                RecordPatientServiceStatus.PatientNotFound,
                (await CreateHandler(context, clinicId: null, data.Admin.Id).HandleAsync(command)).Status);
            Assert.Equal(
                RecordPatientServiceStatus.PatientNotFound,
                (await CreateHandler(context, data.Clinic.Id, userId: null).HandleAsync(command)).Status);
        }

        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task InvalidRequest_IsRejected_AndNothingIsRecorded()
    {
        var data = await SeedAsync();

        var emptyId = await RecordAsync(data, data.PatientA.Id, data.Service.Id, occurrenceId: Guid.Empty);
        var future = await RecordAsync(data, data.PatientA.Id, data.Service.Id, performedAt: DateTime.UtcNow.AddHours(1));
        var notUtc = await RecordAsync(data, data.PatientA.Id, data.Service.Id, performedAt: DateTime.Now.AddHours(-1));

        Assert.Equal(RecordPatientServiceStatus.InvalidRequest, emptyId.Status);
        Assert.Equal(RecordPatientServiceStatus.InvalidRequest, future.Status);
        Assert.Equal(RecordPatientServiceStatus.InvalidRequest, notUtc.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    // --- duplicates -----------------------------------------------------------

    [Fact]
    public async Task RepeatedSubmissionOfTheSameOccurrence_IsRejected_AndEarnsOnlyOnce()
    {
        var data = await SeedAsync();
        var occurrenceId = Guid.NewGuid();

        await RecordAsync(data, data.PatientA.Id, data.Service.Id, occurrenceId);

        var repeated = await RecordAsync(data, data.PatientA.Id, data.Service.Id, occurrenceId);

        Assert.Equal(RecordPatientServiceStatus.AlreadyRecorded, repeated.Status);
        Assert.Equal((75_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task RepeatedSubmissionOfAnOccurrenceWithoutPoints_IsRejected_AndRecordedOnlyOnce()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(data.Clinic.Id, linked: false);
        var occurrenceId = Guid.NewGuid();

        await RecordAsync(data, patient.Id, data.Service.Id, occurrenceId);

        var repeated = await RecordAsync(data, patient.Id, data.Service.Id, occurrenceId);

        Assert.Equal(RecordPatientServiceStatus.AlreadyRecorded, repeated.Status);
        Assert.Equal((0, 0, 1), await ReadStateAsync(patient.Id));
    }

    // A repeat of an occurrence without points that races past the existence
    // check is stopped by the occurrence's unique key.
    [Fact]
    public async Task ConcurrentSubmissionsOfAnOccurrenceWithoutPoints_AreStoppedByTheOccurrenceId()
    {
        var data = await SeedAsync();
        var patient = await SeedPatientAsync(data.Clinic.Id, linked: false);
        var occurrenceId = Guid.NewGuid();

        PatientService NewOccurrence() =>
            new(occurrenceId, patient, data.Service, data.Scoring, activeCycle: null, DateTime.UtcNow, data.Admin.Id);

        await using (var first = CreateContext())
        {
            await new PatientServiceRepository(first).AddAsync(NewOccurrence());
        }

        await using (var second = CreateContext())
        {
            await Assert.ThrowsAsync<DuplicateServiceOccurrenceException>(() =>
                new PatientServiceRepository(second).AddAsync(NewOccurrence()));
        }

        Assert.Equal((0, 0, 1), await ReadStateAsync(patient.Id));
    }

    // An occurrence id already used in another clinic is a conflict too, and it
    // reveals nothing else about that clinic's record.
    [Fact]
    public async Task OccurrenceIdUsedByAnotherClinic_IsRejected_AndNothingIsRecordedHere()
    {
        var data = await SeedAsync();
        var other = await SeedOtherClinicAsync();
        var occurrenceId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            var otherAdmin = new User(other.Clinic.Id, $"admin-b-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.ClinicAdmin);
            context.Users.Add(otherAdmin);
            await context.SaveChangesAsync();

            await CreateHandler(context, other.Clinic.Id, otherAdmin.Id).HandleAsync(
                new RecordPatientServiceCommand(other.Patient.Id, occurrenceId, other.Service.Id, null));
        }

        var result = await RecordAsync(data, data.PatientA.Id, data.Service.Id, occurrenceId);

        Assert.Equal(RecordPatientServiceStatus.AlreadyRecorded, result.Status);
        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
        Assert.Equal((75_000, 1, 1), await ReadStateAsync(other.Patient.Id));
    }

    // Even outside the handler, the database never accepts a second earning
    // transaction for one recorded occurrence.
    [Fact]
    public async Task SecondTransactionForTheSameOccurrence_IsRejectedByTheDatabase()
    {
        var data = await SeedAsync();
        var patientService = data.RecordService(data.PatientA);

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            await repository.SaveServicePointsAsync(patientService, account!, account!.EarnServicePoints(patientService, data.CycleA));
        }

        await using (var context = CreateContext())
        {
            var repository = new LoyaltyAccountRepository(context);
            var account = await repository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
            var again = account!.EarnServicePoints(patientService, data.CycleA);

            await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveBalanceChangesAsync([account], [again]));
        }

        Assert.Equal((75_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // --- concurrency and atomicity --------------------------------------------

    // Two requests for the same occurrence that both read the account before
    // either saved: the second fails and persists nothing, so it earns once.
    [Fact]
    public async Task ConcurrentSubmissionsOfTheSameOccurrence_EarnOnlyOnce()
    {
        var data = await SeedAsync();
        var occurrenceId = Guid.NewGuid();

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);
        var firstAccount = await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var secondAccount = await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);

        var firstService = new PatientService(occurrenceId, data.PatientA, data.Service, data.Scoring, data.CycleA, DateTime.UtcNow, data.Admin.Id);
        var secondService = new PatientService(occurrenceId, data.PatientA, data.Service, data.Scoring, data.CycleA, DateTime.UtcNow, data.Admin.Id);

        await firstRepository.SaveServicePointsAsync(firstService, firstAccount!, firstAccount!.EarnServicePoints(firstService, data.CycleA));

        var secondAttempt = await Record.ExceptionAsync(() =>
            secondRepository.SaveServicePointsAsync(secondService, secondAccount!, secondAccount!.EarnServicePoints(secondService, data.CycleA)));

        // The stale account fails the rowversion check before the duplicate id
        // is reached; either way, nothing from the second attempt is saved.
        Assert.True(secondAttempt is ConcurrencyConflictException or DuplicateServiceOccurrenceException);
        Assert.Equal((75_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    // At the maximum the service changes no balance, but the account's version is
    // still checked (ADR-008), so the stale second attempt conflicts - or, should
    // it get past that, the duplicate occurrence id stops it. Either way nothing
    // from it is saved.
    [Fact]
    public async Task ConcurrentSubmissionsOfTheSameOccurrence_AtTheMaximum_AreStoppedByTheOccurrenceId()
    {
        var data = await SeedAsync();
        await RecordAsync(data, data.PatientA.Id, (await SeedServiceAsync(data.Clinic.Id, 100_000m)).Id);
        var occurrenceId = Guid.NewGuid();

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);
        var firstAccount = await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var secondAccount = await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);

        var firstService = new PatientService(occurrenceId, data.PatientA, data.Service, data.Scoring, data.CycleA, DateTime.UtcNow, data.Admin.Id);
        var secondService = new PatientService(occurrenceId, data.PatientA, data.Service, data.Scoring, data.CycleA, DateTime.UtcNow, data.Admin.Id);

        await firstRepository.SaveServicePointsAsync(firstService, firstAccount!, firstAccount!.EarnServicePoints(firstService, data.CycleA));

        var secondAttempt = await Record.ExceptionAsync(() =>
            secondRepository.SaveServicePointsAsync(secondService, secondAccount!, secondAccount!.EarnServicePoints(secondService, data.CycleA)));

        Assert.True(secondAttempt is ConcurrencyConflictException or DuplicateServiceOccurrenceException);
        Assert.Equal((LoyaltyAccount.MaximumPointsBalance, 2, 2), await ReadStateAsync(data.PatientA.Id));
    }

    // Two different occurrences racing on one account: the rowversion conflict
    // rolls back the second entirely - its service record included.
    [Fact]
    public async Task ConcurrentDifferentOccurrences_SecondConflicts_AndPersistsNothing()
    {
        var data = await SeedAsync();

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstRepository = new LoyaltyAccountRepository(first);
        var secondRepository = new LoyaltyAccountRepository(second);
        var firstAccount = await firstRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);
        var secondAccount = await secondRepository.GetByPatientIdForTenantAsync(data.PatientA.Id, data.Clinic.Id);

        var firstService = data.RecordService(data.PatientA);
        var secondService = data.RecordService(data.PatientA, 3_000m);

        await firstRepository.SaveServicePointsAsync(firstService, firstAccount!, firstAccount!.EarnServicePoints(firstService, data.CycleA));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            secondRepository.SaveServicePointsAsync(secondService, secondAccount!, secondAccount!.EarnServicePoints(secondService, data.CycleA)));

        Assert.Equal((75_000, 1, 1), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task WhenTheTransactionInsertFails_TheServiceRecordAndBalanceAreRolledBack()
    {
        var data = await SeedAsync();
        FailInsertsInto("LoyaltyTransactions");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            RecordAsync(data, data.PatientA.Id, data.Service.Id));

        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

    [Fact]
    public async Task WhenTheServiceRecordInsertFails_TheBalanceIsRolledBack()
    {
        var data = await SeedAsync();
        FailInsertsInto("PatientServices");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            RecordAsync(data, data.PatientA.Id, data.Service.Id));

        Assert.Equal((0, 0, 0), await ReadStateAsync(data.PatientA.Id));
    }

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
