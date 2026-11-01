using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Identity;

public class CreateClinicAdminHandlerTests
{
    [Fact]
    public async Task HandleAsync_CreatesClinicAdmin_ForGivenClinic()
    {
        var userRepository = new InMemoryUserRepository();
        var passwordHasher = new PasswordHasher();
        var handler = new CreateClinicAdminHandler(userRepository, passwordHasher);

        var clinicId = Guid.NewGuid();

        var userId = await handler.HandleAsync(
            new CreateClinicAdminCommand(clinicId, "admin@clinic.test", "Sup3rSecret!"));

        var created = await userRepository.GetByEmailAsync("admin@clinic.test");

        Assert.NotNull(created);
        Assert.Equal(userId, created!.Id);
        Assert.Equal(clinicId, created.ClinicId);
    }
}
