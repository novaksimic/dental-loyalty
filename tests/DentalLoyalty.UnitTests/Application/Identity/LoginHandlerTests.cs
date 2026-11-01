using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Identity;

public class LoginHandlerTests
{
    private static readonly JwtOptions JwtOptions = new()
    {
        SigningKey = "login-handler-tests-signing-key-32-bytes-min!!",
        Issuer = "DentalLoyalty.Tests",
        Audience = "DentalLoyalty.Tests.Client",
        AccessTokenLifetimeMinutes = 60,
    };

    private static LoginHandler CreateHandler(InMemoryUserRepository userRepository)
    {
        return new LoginHandler(
            userRepository,
            new PasswordHasher(),
            new JwtTokenGenerator(Options.Create(JwtOptions)));
    }

    [Fact]
    public async Task HandleAsync_WithValidCredentials_ReturnsLoginResponseWithToken()
    {
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(null, "admin@platform.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.PlatformAdmin);
        await userRepository.AddAsync(user);

        var handler = CreateHandler(userRepository);

        var result = await handler.HandleAsync(new LoginCommand("admin@platform.test", "Sup3rSecret!"));

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.Token));
        Assert.Equal(user.Id, result.UserId);
    }

    [Fact]
    public async Task HandleAsync_WithWrongPassword_ReturnsNull()
    {
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(null, "admin@platform.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.PlatformAdmin);
        await userRepository.AddAsync(user);

        var handler = CreateHandler(userRepository);

        var result = await handler.HandleAsync(new LoginCommand("admin@platform.test", "WrongPassword!"));

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEmail_ReturnsNull()
    {
        var handler = CreateHandler(new InMemoryUserRepository());

        var result = await handler.HandleAsync(new LoginCommand("nobody@platform.test", "Sup3rSecret!"));

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_WithInactiveUser_ReturnsNull()
    {
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(null, "admin@platform.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.PlatformAdmin);
        user.Deactivate();
        await userRepository.AddAsync(user);

        var handler = CreateHandler(userRepository);

        var result = await handler.HandleAsync(new LoginCommand("admin@platform.test", "Sup3rSecret!"));

        Assert.Null(result);
    }

    [Fact]
    public async Task HandleAsync_ForPlatformAdmin_TokenContainsUserIdAndRole_AndNoClinicId()
    {
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(null, "admin@platform.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.PlatformAdmin);
        await userRepository.AddAsync(user);

        var handler = CreateHandler(userRepository);
        var result = await handler.HandleAsync(new LoginCommand("admin@platform.test", "Sup3rSecret!"));

        Assert.NotNull(result);
        Assert.Equal(nameof(UserRole.PlatformAdmin), result!.Role);
        Assert.Null(result.ClinicId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == JwtClaimNames.UserId).Value);
        Assert.Equal(nameof(UserRole.PlatformAdmin), jwt.Claims.Single(c => c.Type == JwtClaimNames.Role).Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == JwtClaimNames.ClinicId);
    }

    [Fact]
    public async Task HandleAsync_ForClinicAdmin_TokenContainsUserIdRoleAndClinicId()
    {
        var clinicId = Guid.NewGuid();
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(clinicId, "admin@clinic.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.ClinicAdmin);
        await userRepository.AddAsync(user);

        var handler = CreateHandler(userRepository);
        var result = await handler.HandleAsync(new LoginCommand("admin@clinic.test", "Sup3rSecret!"));

        Assert.NotNull(result);
        Assert.Equal(nameof(UserRole.ClinicAdmin), result!.Role);
        Assert.Equal(clinicId, result.ClinicId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal(clinicId.ToString(), jwt.Claims.Single(c => c.Type == JwtClaimNames.ClinicId).Value);
    }

    [Fact]
    public async Task HandleAsync_ForPatient_TokenContainsUserIdRoleAndClinicId()
    {
        var clinicId = Guid.NewGuid();
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(clinicId, "patient@clinic.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.Patient);
        await userRepository.AddAsync(user);

        var handler = CreateHandler(userRepository);
        var result = await handler.HandleAsync(new LoginCommand("patient@clinic.test", "Sup3rSecret!"));

        Assert.NotNull(result);
        Assert.Equal(nameof(UserRole.Patient), result!.Role);
        Assert.Equal(clinicId, result.ClinicId);
    }
}
