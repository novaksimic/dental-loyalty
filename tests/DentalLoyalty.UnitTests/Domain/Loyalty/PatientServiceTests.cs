using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Exceptions;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

public class PatientServiceTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid AdminUserId = Guid.NewGuid();

    // The clinic's scoring configuration, at the platform default (100 RSD = 1,000 points).
    private static readonly LoyaltyConfiguration Scoring = LoyaltyConfiguration.CreateDefault(ClinicId);

    private static Patient NewPatient(Guid? clinicId = null) =>
        new(clinicId ?? ClinicId, "Ana", "Petrovic", "+381600000000");

    // A registered, linked patient (BUSINESS.md §5.1).
    private static Patient NewLinkedPatient()
    {
        var patient = NewPatient();
        patient.LinkUser(Guid.NewGuid());
        return patient;
    }

    private static PatientService Record(
        Patient patient,
        Service service,
        LoyaltyCycle? cycle,
        DateTime? performedAt = null,
        LoyaltyConfiguration? scoring = null) =>
        new(Guid.NewGuid(), patient, service, scoring ?? Scoring, cycle, performedAt ?? DateTime.UtcNow, AdminUserId);

    [Fact]
    public void EligibleService_CalculatesPointsFromCatalogPrice_AndRecordsTheOccurrence()
    {
        var patient = NewLinkedPatient();
        var cycle = LoyaltyCycle.StartFirst(patient);
        var service = new Service(ClinicId, "Kompozitni ispun", 7_500m);
        var id = Guid.NewGuid();
        var performedAt = DateTime.UtcNow;

        var before = DateTime.UtcNow;
        var patientService = new PatientService(id, patient, service, Scoring, cycle, performedAt, AdminUserId);

        Assert.Equal(id, patientService.Id);
        Assert.Equal(7_500m, patientService.PriceAtTimeOfService);
        Assert.Equal(75_000, patientService.EarnedPoints);
        Assert.Equal(patient.Id, patientService.PatientId);
        Assert.Equal(service.Id, patientService.ServiceId);
        Assert.Equal(ClinicId, patientService.ClinicId);
        Assert.Equal(performedAt, patientService.UsedAt);
        Assert.InRange(patientService.RecordedAt, before, DateTime.UtcNow);
        Assert.Equal(AdminUserId, patientService.RecordedBy);
        Assert.True(cycle.AcceptsService(patientService));
    }

    [Fact]
    public void EligibleService_WithPriceYieldingFractionalPoints_RoundsPoints()
    {
        var patient = NewLinkedPatient();

        var patientService = Record(patient, new Service(ClinicId, "Pregled", 1_234.56m), LoyaltyCycle.StartFirst(patient));

        // BUSINESS.md §3.2: 12,345.6 rounds to 12,346; the amount itself is not rounded.
        Assert.Equal(1_234.56m, patientService.PriceAtTimeOfService);
        Assert.Equal(12_346, patientService.EarnedPoints);
    }

    // BUSINESS.md §3.1: points use the scoring configuration of the service's clinic.
    [Fact]
    public void EligibleService_UsesTheClinicsOwnScoringConfiguration()
    {
        var patient = NewLinkedPatient();
        var clinicScoring = new LoyaltyConfiguration(ClinicId, 100m, 500);

        var patientService = Record(
            patient, new Service(ClinicId, "Kompozitni ispun", 7_500m), LoyaltyCycle.StartFirst(patient), scoring: clinicScoring);

        Assert.Equal(37_500, patientService.EarnedPoints);
    }

    [Fact]
    public void EligibleService_WithZeroPrice_GeneratesZeroPoints_ButIsStillAccepted()
    {
        var patient = NewLinkedPatient();
        var cycle = LoyaltyCycle.StartFirst(patient);

        var patientService = Record(patient, new Service(ClinicId, "Kontrola", 0m), cycle);

        Assert.Equal(0, patientService.EarnedPoints);
        Assert.True(cycle.AcceptsService(patientService));
    }

    // BUSINESS.md §3.1, §5.1: before registration and linking there is no cycle,
    // and the service is recorded without points.
    [Fact]
    public void ServiceForUnlinkedPatient_IsRecordedWithoutPoints()
    {
        var patientService = Record(NewPatient(), new Service(ClinicId, "Pregled", 2_000m), cycle: null);

        Assert.Equal(0, patientService.EarnedPoints);
        Assert.Equal(2_000m, patientService.PriceAtTimeOfService);
    }

    [Fact]
    public void ServiceForLinkedPatientWithoutActiveCycle_IsRecordedWithoutPoints()
    {
        var patientService = Record(NewLinkedPatient(), new Service(ClinicId, "Pregled", 2_000m), cycle: null);

        Assert.Equal(0, patientService.EarnedPoints);
    }

    // BUSINESS.md §5.1: a service performed before the cycle started is never
    // credited retroactively, even when it is recorded later.
    [Fact]
    public void ServicePerformedBeforeTheCycleStarted_IsRecordedWithoutPoints()
    {
        var patient = NewLinkedPatient();
        var cycle = LoyaltyCycle.StartFirst(patient);

        var patientService = Record(patient, new Service(ClinicId, "Pregled", 2_000m), cycle, cycle.StartedAt.AddTicks(-1));

        Assert.Equal(0, patientService.EarnedPoints);
        Assert.False(cycle.AcceptsService(patientService));
    }

    [Fact]
    public void ServicePerformedExactlyWhenTheCycleStarted_EarnsPoints()
    {
        var patient = NewLinkedPatient();
        var cycle = LoyaltyCycle.StartFirst(patient);

        var patientService = Record(patient, new Service(ClinicId, "Pregled", 2_000m), cycle, cycle.StartedAt);

        Assert.Equal(20_000, patientService.EarnedPoints);
    }

    [Fact]
    public void ServiceInTheLastMinuteOfTheCycle_EarnsPoints()
    {
        var patient = NewLinkedPatient();
        var cycle = LoyaltyCycle.StartFirst(patient).StartedOn(DateTime.UtcNow.AddYears(-1).AddMinutes(1));

        var patientService = Record(patient, new Service(ClinicId, "Pregled", 2_000m), cycle);

        Assert.Equal(20_000, patientService.EarnedPoints);
    }

    // BUSINESS.md §5.3: points must never go to an expired cycle.
    [Fact]
    public void ServiceRecordedAfterTheCycleExpired_IsRejected()
    {
        var patient = NewLinkedPatient();
        var cycle = LoyaltyCycle.StartFirst(patient).StartedOn(DateTime.UtcNow.AddYears(-1).AddMinutes(-1));

        Assert.Throws<DomainException>(() => Record(patient, new Service(ClinicId, "Pregled", 2_000m), cycle));
    }

    [Fact]
    public void ServiceWithAnotherPatientsCycle_IsRejected()
    {
        var otherPatient = NewLinkedPatient();

        Assert.Throws<DomainException>(() =>
            Record(NewLinkedPatient(), new Service(ClinicId, "Pregled", 2_000m), LoyaltyCycle.StartFirst(otherPatient)));
    }

    [Fact]
    public void ServiceForInactivePatient_IsRejected()
    {
        var patient = NewLinkedPatient();
        var cycle = LoyaltyCycle.StartFirst(patient);
        patient.Deactivate();

        Assert.Throws<DomainException>(() => Record(patient, new Service(ClinicId, "Pregled", 2_000m), cycle));
    }

    [Fact]
    public void InactiveCatalogService_IsRejected()
    {
        var patient = NewLinkedPatient();
        var service = new Service(ClinicId, "Pregled", 2_000m);
        service.Deactivate();

        Assert.Throws<DomainException>(() => Record(patient, service, LoyaltyCycle.StartFirst(patient)));
    }

    [Fact]
    public void ServiceFromAnotherClinic_IsRejected()
    {
        var service = new Service(Guid.NewGuid(), "Pregled", 2_000m);

        Assert.Throws<DomainException>(() => Record(NewPatient(), service, cycle: null));
    }

    // BUSINESS.md §3.1: a clinic's scoring configuration is never applied to
    // another clinic's services.
    [Fact]
    public void AnotherClinicsScoringConfiguration_IsRejected()
    {
        var service = new Service(ClinicId, "Pregled", 2_000m);

        Assert.Throws<DomainException>(() =>
            Record(NewPatient(), service, cycle: null, scoring: LoyaltyConfiguration.CreateDefault(Guid.NewGuid())));
    }

    [Fact]
    public void MissingScoringConfiguration_IsRejected()
    {
        var service = new Service(ClinicId, "Pregled", 2_000m);

        Assert.Throws<ArgumentNullException>(() =>
            new PatientService(Guid.NewGuid(), NewPatient(), service, null!, null, DateTime.UtcNow, AdminUserId));
    }

    [Fact]
    public void PerformanceTimeInTheFutureOrNotUtc_IsRejected()
    {
        var service = new Service(ClinicId, "Pregled", 2_000m);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Record(NewPatient(), service, cycle: null, performedAt: DateTime.UtcNow.AddMinutes(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Record(NewPatient(), service, cycle: null, performedAt: DateTime.Now.AddMinutes(-5)));
    }

    [Fact]
    public void MissingOccurrenceIdOrRecordingUser_IsRejected()
    {
        var service = new Service(ClinicId, "Pregled", 2_000m);

        Assert.Throws<ArgumentException>(() =>
            new PatientService(Guid.Empty, NewPatient(), service, Scoring, null, DateTime.UtcNow, AdminUserId));
        Assert.Throws<ArgumentException>(() =>
            new PatientService(Guid.NewGuid(), NewPatient(), service, Scoring, null, DateTime.UtcNow, Guid.Empty));
    }
}
