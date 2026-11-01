using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Identity;

public class CreatePlatformAdminHandlerTests
{
    [Fact]
    public async Task HandleAsync_CreatesPlatformAdmin_WithHashedPassword()
    {
        var userRepository = new InMemoryUserRepository();
        var passwordHasher = new PasswordHasher();
        var handler = new CreatePlatformAdminHandler(userRepository, passwordHasher);

        var userId = await handler.HandleAsync(
            new CreatePlatformAdminCommand("admin@platform.test", "Sup3rSecret!"));

        var created = await userRepository.GetByEmailAsync("admin@platform.test");

        Assert.NotNull(created);
        Assert.Equal(userId, created!.Id);
        Assert.NotEqual("Sup3rSecret!", created.PasswordHash);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateEmail_ThrowsInvalidOperationException()
    {
        var userRepository = new InMemoryUserRepository();
        var passwordHasher = new PasswordHasher();
        var handler = new CreatePlatformAdminHandler(userRepository, passwordHasher);

        await handler.HandleAsync(
            new CreatePlatformAdminCommand("admin@platform.test", "Sup3rSecret!"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new CreatePlatformAdminCommand("admin@platform.test", "AnotherPassword!")));
    }
}
