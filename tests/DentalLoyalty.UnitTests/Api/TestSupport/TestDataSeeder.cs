using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.TestSupport;

// Seeds data directly into the factory's in-memory DbContext, bypassing HTTP -
// tenant-isolation tests need to prove cross-tenant data exists and is reachable
// only through the boundary they're testing, not avoid creating it.
public static class TestDataSeeder
{
    public static async Task<(Guid ClinicId, Guid PatientId)> SeedClinicWithPatientAsync(
        DentalLoyaltyApiFactory factory,
        string clinicName,
        string patientFirstName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var clinic = new Clinic(clinicName);
        var patient = new Patient(clinic.Id, patientFirstName, "TestPatient", "+15550000");

        db.Clinics.Add(clinic);
        db.Patients.Add(patient);
        db.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));
        await db.SaveChangesAsync();

        return (clinic.Id, patient.Id);
    }

    // Creates a Patient-role User in clinicId and links it to a new Patient in the
    // same clinic, with the Patient's account and first cycle (mirroring
    // CreatePatientHandler + CreatePatientUserHandler), returning the UserId to
    // put in the caller's JWT.
    public static async Task<(Guid UserId, Guid PatientId)> SeedPatientWithUserAsync(
        DentalLoyaltyApiFactory factory,
        Guid clinicId,
        string patientFirstName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var user = new User(
            clinicId,
            $"{patientFirstName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            "not-a-real-hash",
            UserRole.Patient);
        var patient = new Patient(clinicId, patientFirstName, "TestPatient", "+15550000");
        patient.LinkUser(user.Id);

        db.Users.Add(user);
        db.Patients.Add(patient);
        db.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));
        db.LoyaltyCycles.Add(LoyaltyCycle.StartFirst(patient));
        await db.SaveChangesAsync();

        return (user.Id, patient.Id);
    }

    // Seeds a clinic together with its scoring configuration, as CreateClinicHandler
    // provisions it.
    public static async Task<Guid> SeedClinicWithLoyaltyConfigurationAsync(
        DentalLoyaltyApiFactory factory,
        string clinicName,
        decimal spendAmountRsd,
        int pointsAwarded)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var clinic = new Clinic(clinicName);

        db.Clinics.Add(clinic);
        db.LoyaltyConfigurations.Add(new LoyaltyConfiguration(clinic.Id, spendAmountRsd, pointsAwarded));
        await db.SaveChangesAsync();

        return clinic.Id;
    }

    public static async Task<LoyaltyConfiguration?> GetLoyaltyConfigurationAsync(DentalLoyaltyApiFactory factory, Guid clinicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.LoyaltyConfigurations.SingleOrDefaultAsync(lc => lc.ClinicId == clinicId);
    }

    // A catalog service of the clinic (BUSINESS.md §15).
    public static async Task<Guid> SeedServiceAsync(
        DentalLoyaltyApiFactory factory,
        Guid clinicId,
        decimal priceRsd,
        bool active = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var service = new Service(clinicId, $"Usluga {priceRsd}", priceRsd);

        if (!active)
        {
            service.Deactivate();
        }

        db.Services.Add(service);
        await db.SaveChangesAsync();

        return service.Id;
    }

    // A Patient of an existing clinic, with its account (as CreatePatientHandler
    // creates it) but not registered or linked - so with no loyalty cycle.
    public static async Task<Guid> SeedUnlinkedPatientAsync(DentalLoyaltyApiFactory factory, Guid clinicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var patient = new Patient(clinicId, "Unlinked", "TestPatient", "+15550000");
        db.Patients.Add(patient);
        db.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));
        await db.SaveChangesAsync();

        return patient.Id;
    }

    // A reward definition of the clinic (BUSINESS.md §10).
    public static async Task<Guid> SeedRewardAsync(
        DentalLoyaltyApiFactory factory,
        Guid clinicId,
        string name,
        int requiredPoints,
        int requiredReferrals)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var reward = new Reward(clinicId, name, requiredPoints, requiredReferrals);
        db.Rewards.Add(reward);
        await db.SaveChangesAsync();

        return reward.Id;
    }

    // Sets the patient's balances directly, bypassing the operations that earn them.
    public static async Task SetLoyaltyBalancesAsync(DentalLoyaltyApiFactory factory, Guid patientId, int points, int referrals)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        (await db.LoyaltyAccounts.SingleAsync(a => a.PatientId == patientId)).WithBalances(points, referrals);
        await db.SaveChangesAsync();
    }

    public static async Task<List<RewardRedemption>> GetRewardRedemptionsAsync(DentalLoyaltyApiFactory factory, Guid patientId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.RewardRedemptions.Where(rr => rr.PatientId == patientId).ToListAsync();
    }

    public static async Task<List<Referral>> GetReferralsAsync(DentalLoyaltyApiFactory factory, Guid referredPatientId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.Referrals.Where(r => r.ReferredPatientId == referredPatientId).ToListAsync();
    }

    public static async Task<List<PatientService>> GetPatientServicesAsync(DentalLoyaltyApiFactory factory, Guid patientId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.PatientServices.Where(ps => ps.PatientId == patientId).ToListAsync();
    }

    public static async Task<List<LoyaltyTransaction>> GetLoyaltyTransactionsAsync(DentalLoyaltyApiFactory factory, Guid patientId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.LoyaltyTransactions.Where(t => t.PatientId == patientId).ToListAsync();
    }

    public static async Task<List<LoyaltyAccount>> GetLoyaltyAccountsAsync(DentalLoyaltyApiFactory factory, Guid patientId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.LoyaltyAccounts.Where(a => a.PatientId == patientId).ToListAsync();
    }

    public static async Task<List<LoyaltyCycle>> GetLoyaltyCyclesAsync(DentalLoyaltyApiFactory factory, Guid patientId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.LoyaltyCycles.Where(c => c.PatientId == patientId).ToListAsync();
    }

    public static async Task<Patient?> GetPatientAsync(DentalLoyaltyApiFactory factory, Guid patientId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        return await db.Patients.FindAsync(patientId);
    }
}
