using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

// Proves the tenant/ownership boundary is enforced by the query itself (not by a
// caller remembering to check afterwards) against a real EF Core-backed context.
public class PatientRepositoryTenantTests
{
    private static DentalLoyaltyDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DentalLoyaltyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new DentalLoyaltyDbContext(options);
    }

    [Fact]
    public async Task GetByIdForTenantAsync_OwnTenantPatient_ReturnsPatient()
    {
        await using var context = CreateContext();
        var repository = new PatientRepository(context);

        var clinicA = Guid.NewGuid();
        var patient = new Patient(clinicA, "Alice", "Anderson", "+15550100");
        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        var result = await repository.GetByIdForTenantAsync(patient.Id, clinicA);

        Assert.NotNull(result);
        Assert.Equal(patient.Id, result!.Id);
    }

    [Fact]
    public async Task GetByIdForTenantAsync_OtherTenantPatient_ReturnsNull()
    {
        await using var context = CreateContext();
        var repository = new PatientRepository(context);

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var patientB = new Patient(clinicB, "Bob", "Baker", "+15550101");
        context.Patients.Add(patientB);
        await context.SaveChangesAsync();

        // Clinic A's tenant context, Clinic B's patient id - must not resolve.
        var result = await repository.GetByIdForTenantAsync(patientB.Id, clinicA);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdForPatientOwnerAsync_SameTenant_DifferentOwner_ReturnsNull()
    {
        await using var context = CreateContext();
        var repository = new PatientRepository(context);

        var clinicA = Guid.NewGuid();
        var patientA1 = new Patient(clinicA, "Alice", "Anderson", "+15550100");
        var patientA2 = new Patient(clinicA, "Amy", "Adams", "+15550102");

        var patientA1UserId = Guid.NewGuid();
        var patientA2UserId = Guid.NewGuid();
        patientA1.LinkUser(patientA1UserId);
        patientA2.LinkUser(patientA2UserId);

        context.Patients.AddRange(patientA1, patientA2);
        await context.SaveChangesAsync();

        // Same clinic, but patientA2's own user id must not unlock patientA1 -
        // tenant match alone is not sufficient for patient-to-patient access.
        var result = await repository.GetByIdForPatientOwnerAsync(
            patientA1.Id, clinicA, patientA2UserId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdForPatientOwnerAsync_OwnRecord_ReturnsPatient()
    {
        await using var context = CreateContext();
        var repository = new PatientRepository(context);

        var clinicA = Guid.NewGuid();
        var patient = new Patient(clinicA, "Alice", "Anderson", "+15550100");
        var userId = Guid.NewGuid();
        patient.LinkUser(userId);

        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        var result = await repository.GetByIdForPatientOwnerAsync(patient.Id, clinicA, userId);

        Assert.NotNull(result);
        Assert.Equal(patient.Id, result!.Id);
    }

    [Fact]
    public async Task GetByUserIdForTenantAsync_OwnRecord_ReturnsPatient()
    {
        await using var context = CreateContext();
        var repository = new PatientRepository(context);

        var clinicA = Guid.NewGuid();
        var patient = new Patient(clinicA, "Alice", "Anderson", "+15550100");
        var other = new Patient(clinicA, "Amy", "Adams", "+15550102");
        var userId = Guid.NewGuid();
        patient.LinkUser(userId);
        other.LinkUser(Guid.NewGuid());

        context.Patients.AddRange(patient, other);
        await context.SaveChangesAsync();

        var result = await repository.GetByUserIdForTenantAsync(userId, clinicA);

        Assert.NotNull(result);
        Assert.Equal(patient.Id, result!.Id);
    }

    [Fact]
    public async Task GetByUserIdForTenantAsync_OwnRecordInOtherTenant_ReturnsNull()
    {
        await using var context = CreateContext();
        var repository = new PatientRepository(context);

        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var patientB = new Patient(clinicB, "Bob", "Baker", "+15550101");
        var userId = Guid.NewGuid();
        patientB.LinkUser(userId);

        context.Patients.Add(patientB);
        await context.SaveChangesAsync();

        // Correct owner, wrong tenant - ownership must not cross the tenant boundary.
        var result = await repository.GetByUserIdForTenantAsync(userId, clinicA);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByUserIdForTenantAsync_NoLinkedRecord_ReturnsNull()
    {
        await using var context = CreateContext();
        var repository = new PatientRepository(context);

        var clinicA = Guid.NewGuid();
        context.Patients.Add(new Patient(clinicA, "Alice", "Anderson", "+15550100"));
        await context.SaveChangesAsync();

        var result = await repository.GetByUserIdForTenantAsync(Guid.NewGuid(), clinicA);

        Assert.Null(result);
    }
}
