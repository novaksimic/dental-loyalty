using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Xunit;

namespace DentalLoyalty.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void PlatformAdmin_WithClinicId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new User(Guid.NewGuid(), "admin@platform.test", "hash", UserRole.PlatformAdmin));
    }

    [Fact]
    public void PlatformAdmin_WithoutClinicId_IsCreatedSuccessfully()
    {
        var user = new User(null, "admin@platform.test", "hash", UserRole.PlatformAdmin);

        Assert.Null(user.ClinicId);
        Assert.Equal(UserRole.PlatformAdmin, user.Role);
    }

    [Fact]
    public void ClinicAdmin_WithoutClinicId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new User(null, "admin@clinic.test", "hash", UserRole.ClinicAdmin));
    }

    [Fact]
    public void Patient_WithoutClinicId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new User(null, "patient@clinic.test", "hash", UserRole.Patient));
    }
}
