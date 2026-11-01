using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;

namespace DentalLoyalty.UnitTests.Infrastructure;

// One clinic with an admin, catalog services and two registered, linked patients
// that each have a loyalty account and an active first cycle - the rows a
// balance-changing operation and its transaction records reference. Shared by
// the SQLite and SQL Server persistence tests.
internal sealed class LoyaltyAccountTestData
{
    // Catalog prices the tests record services at (points come from the catalog
    // price only).
    public const decimal DefaultPriceRsd = 7_500m;

    private static readonly decimal[] CatalogPricesRsd = [1_000m, 3_000m, DefaultPriceRsd, 100_000m];

    private readonly Dictionary<decimal, Service> _servicesByPrice;

    private LoyaltyAccountTestData(
        Clinic clinic,
        LoyaltyConfiguration scoring,
        User admin,
        Dictionary<decimal, Service> servicesByPrice,
        Patient patientA,
        Patient patientB,
        LoyaltyCycle cycleA,
        LoyaltyCycle cycleB)
    {
        Clinic = clinic;
        Scoring = scoring;
        Admin = admin;
        _servicesByPrice = servicesByPrice;
        PatientA = patientA;
        PatientB = patientB;
        CycleA = cycleA;
        CycleB = cycleB;
    }

    public Clinic Clinic { get; }

    public LoyaltyConfiguration Scoring { get; }

    public User Admin { get; }

    public Service Service => _servicesByPrice[DefaultPriceRsd];

    public Patient PatientA { get; }

    public Patient PatientB { get; }

    public LoyaltyCycle CycleA { get; }

    public LoyaltyCycle CycleB { get; }

    public static async Task<LoyaltyAccountTestData> SeedAsync(DentalLoyaltyDbContext context)
    {
        var clinic = new Clinic("Clinic A");
        var scoring = LoyaltyConfiguration.CreateDefault(clinic.Id);
        var admin = new User(clinic.Id, $"admin-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.ClinicAdmin);
        var services = CatalogPricesRsd.ToDictionary(
            price => price,
            price => new Service(clinic.Id, $"Usluga {price}", price));

        var userA = new User(clinic.Id, $"ana-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);
        var userB = new User(clinic.Id, $"marko-{Guid.NewGuid():N}@example.com", "not-a-real-hash", UserRole.Patient);
        var patientA = new Patient(clinic.Id, "Ana", "Petrovic", "+381600000000");
        var patientB = new Patient(clinic.Id, "Marko", "Jovanovic", "+381600000001");
        patientA.LinkUser(userA.Id);
        patientB.LinkUser(userB.Id);
        var cycleA = LoyaltyCycle.StartFirst(patientA);
        var cycleB = LoyaltyCycle.StartFirst(patientB);

        context.Clinics.Add(clinic);
        context.LoyaltyConfigurations.Add(scoring);
        context.Users.AddRange(admin, userA, userB);
        context.Services.AddRange(services.Values);
        context.Patients.AddRange(patientA, patientB);
        context.LoyaltyAccounts.AddRange(
            LoyaltyAccount.OpenFor(patientA),
            LoyaltyAccount.OpenFor(patientB));
        context.LoyaltyCycles.AddRange(cycleA, cycleB);

        await context.SaveChangesAsync();

        return new LoyaltyAccountTestData(clinic, scoring, admin, services, patientA, patientB, cycleA, cycleB);
    }

    public LoyaltyCycle CycleOf(Patient patient) =>
        patient.Id == PatientA.Id ? CycleA : CycleB;

    // Records a new service occurrence for the patient in the given unit of work -
    // the earning transaction references it - and returns the account's
    // ServicePointsEarned transaction for it.
    public LoyaltyTransaction Earn(
        DentalLoyaltyDbContext context,
        LoyaltyAccount account,
        Patient patient,
        decimal priceRsd = DefaultPriceRsd)
    {
        var patientService = RecordService(patient, priceRsd);
        context.PatientServices.Add(patientService);
        return account.EarnServicePoints(patientService, CycleOf(patient));
    }

    // A new occurrence of the catalog service with the given price, performed now
    // and recorded by the admin, within the patient's active cycle. Not persisted.
    public PatientService RecordService(Patient patient, decimal priceRsd = DefaultPriceRsd) =>
        new(
            Guid.NewGuid(),
            patient,
            _servicesByPrice[priceRsd],
            Scoring,
            CycleOf(patient),
            DateTime.UtcNow,
            Admin.Id);
}
