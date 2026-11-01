using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Exceptions;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain;

// A Patient is linked to at most one User, and that link is never handed over.
public class PatientTests
{
    private static Patient NewPatient() => new(Guid.NewGuid(), "Ana", "Petrovic", "+381600000000");

    [Fact]
    public void LinkUser_UnlinkedPatient_LinksTheUser()
    {
        var patient = NewPatient();
        var userId = Guid.NewGuid();

        patient.LinkUser(userId);

        Assert.Equal(userId, patient.UserId);
        Assert.NotNull(patient.UpdatedAt);
    }

    [Fact]
    public void LinkUser_SameUserAgain_ChangesNothing()
    {
        var patient = NewPatient();
        var userId = Guid.NewGuid();
        patient.LinkUser(userId);
        var updatedAt = patient.UpdatedAt;

        patient.LinkUser(userId);

        Assert.Equal(userId, patient.UserId);
        Assert.Equal(updatedAt, patient.UpdatedAt);
    }

    [Fact]
    public void LinkUser_AnotherUser_IsRejected_AndKeepsTheExistingLink()
    {
        var patient = NewPatient();
        var userId = Guid.NewGuid();
        patient.LinkUser(userId);
        var updatedAt = patient.UpdatedAt;

        Assert.Throws<DomainException>(() => patient.LinkUser(Guid.NewGuid()));

        Assert.Equal(userId, patient.UserId);
        Assert.Equal(updatedAt, patient.UpdatedAt);
    }
}
