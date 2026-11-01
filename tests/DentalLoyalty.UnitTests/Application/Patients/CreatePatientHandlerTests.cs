using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Patients;

public class CreatePatientHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithTenant_IgnoresCommandClinicId_UsesCurrentTenant()
    {
        var ownClinicId = Guid.NewGuid();
        var attackerSuppliedClinicId = Guid.NewGuid();

        var repository = new InMemoryPatientRepository();
        var handler = new CreatePatientHandler(
            repository,
            FakeCurrentTenant.ForClinic(ownClinicId));

        var patientId = await handler.HandleAsync(
            new CreatePatientCommand(
                attackerSuppliedClinicId,
                "Ada",
                "Lovelace",
                "+15550100",
                null));

        var created = await repository.GetByIdAsync(patientId);

        Assert.NotNull(created);
        Assert.Equal(ownClinicId, created!.ClinicId);
        Assert.NotEqual(attackerSuppliedClinicId, created.ClinicId);
    }

    [Fact]
    public async Task HandleAsync_WithoutTenant_UsesCommandClinicId()
    {
        var targetClinicId = Guid.NewGuid();

        var repository = new InMemoryPatientRepository();
        var handler = new CreatePatientHandler(
            repository,
            FakeCurrentTenant.NoTenant());

        var patientId = await handler.HandleAsync(
            new CreatePatientCommand(
                targetClinicId,
                "Grace",
                "Hopper",
                "+15550101",
                null));

        var created = await repository.GetByIdAsync(patientId);

        Assert.NotNull(created);
        Assert.Equal(targetClinicId, created!.ClinicId);
    }

    // ADR-006 §1, §3, §4: the account is created with the Patient, in the
    // Patient's own clinic - the tenant, not the client-supplied ClinicId -
    // starting at 0 points, and no loyalty cycle starts (BUSINESS.md §5.1).
    [Fact]
    public async Task HandleAsync_WithTenant_CreatesZeroBalanceAccountInOwnClinic_AndNoCycle()
    {
        var ownClinicId = Guid.NewGuid();

        var repository = new InMemoryPatientRepository();
        var handler = new CreatePatientHandler(
            repository,
            FakeCurrentTenant.ForClinic(ownClinicId));

        var patientId = await handler.HandleAsync(
            new CreatePatientCommand(Guid.NewGuid(), "Ada", "Lovelace", "+15550100", null));

        var account = Assert.Single(repository.LoyaltyAccounts);
        Assert.Equal(patientId, account.PatientId);
        Assert.Equal(ownClinicId, account.ClinicId);
        Assert.Equal(0, account.Points);
        Assert.Empty(repository.LoyaltyCycles);
    }

    [Fact]
    public async Task HandleAsync_WithoutTenant_CreatesAccountInTheRequestedClinic()
    {
        var targetClinicId = Guid.NewGuid();

        var repository = new InMemoryPatientRepository();
        var handler = new CreatePatientHandler(
            repository,
            FakeCurrentTenant.NoTenant());

        var patientId = await handler.HandleAsync(
            new CreatePatientCommand(targetClinicId, "Grace", "Hopper", "+15550101", null));

        var account = Assert.Single(repository.LoyaltyAccounts);
        Assert.Equal(patientId, account.PatientId);
        Assert.Equal(targetClinicId, account.ClinicId);
        Assert.Equal(0, account.Points);
    }
}
