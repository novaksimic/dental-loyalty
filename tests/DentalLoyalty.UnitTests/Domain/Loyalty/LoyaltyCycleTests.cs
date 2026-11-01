using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain.Loyalty;

// BUSINESS.md §5.1: the first cycle starts only when registration and account
// linking are successfully completed.
public class LoyaltyCycleTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();

    [Fact]
    public void StartFirst_ForLinkedPatient_StartsActiveCycleForThatPatientAndClinic()
    {
        var patient = new Patient(ClinicId, "Ana", "Petrovic", "+381600000000");
        patient.LinkUser(Guid.NewGuid());

        var before = DateTime.UtcNow;
        var cycle = LoyaltyCycle.StartFirst(patient);
        var after = DateTime.UtcNow;

        Assert.NotEqual(Guid.Empty, cycle.Id);
        Assert.Equal(patient.Id, cycle.PatientId);
        Assert.Equal(ClinicId, cycle.ClinicId);
        Assert.Equal(LoyaltyCycleStatus.Active, cycle.Status);
        Assert.InRange(cycle.StartedAt, before, after);
    }

    [Fact]
    public void StartFirst_ForPatientWithoutLinkedUser_IsRejected()
    {
        var patient = new Patient(ClinicId, "Ana", "Petrovic", "+381600000000");

        Assert.Throws<DomainException>(() => LoyaltyCycle.StartFirst(patient));
    }

    // BUSINESS.md §5.1/§5.3: a cycle lasts one year and expires one year after
    // its start date.
    [Fact]
    public void Cycle_ExpiresOneYearAfterItsStart()
    {
        var patient = new Patient(ClinicId, "Ana", "Petrovic", "+381600000000");
        patient.LinkUser(Guid.NewGuid());
        var cycle = LoyaltyCycle.StartFirst(patient);

        Assert.Equal(cycle.StartedAt.AddYears(1), cycle.ExpiresAt);
        Assert.False(cycle.HasExpiredAt(cycle.ExpiresAt.AddTicks(-1)));
        Assert.True(cycle.HasExpiredAt(cycle.ExpiresAt));
    }

    [Fact]
    public void StartFirst_WithoutPatient_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => LoyaltyCycle.StartFirst(null!));
    }
}
