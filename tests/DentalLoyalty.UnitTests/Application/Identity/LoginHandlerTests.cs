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

    private static LoginHandler CreateHandler(
        InMemoryUserRepository userRepository,
        InMemoryPatientRepository? patientRepository = null)
    {
        return new LoginHandler(
            userRepository,
            new PasswordHasher(),
            new JwtTokenGenerator(Options.Create(JwtOptions)),
            patientRepository ?? new InMemoryPatientRepository(userRepository));
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

    // ADR-010 decision 4: accounts created through the API are stored
    // lower-cased, so any casing of the email signs in.
    [Theory]
    [InlineData("ana@patient.test")]
    [InlineData("ANA@Patient.TEST")]
    [InlineData("  Ana@patient.test  ")]
    public async Task HandleAsync_LowerCaseAccount_SignsInWithAnyCasing(string typed)
    {
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(Guid.NewGuid(), "ana@patient.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.Patient);
        await userRepository.AddAsync(user);

        var result = await CreateHandler(userRepository).HandleAsync(new LoginCommand(typed, "Sup3rSecret!"));

        Assert.Equal(user.Id, result?.UserId);
    }

    // An existing account stored in mixed case keeps signing in as typed today.
    [Theory]
    [InlineData("Ana.Admin@Platform.test")]
    [InlineData("  Ana.Admin@Platform.test  ")]
    public async Task HandleAsync_MixedCaseAccount_SignsInWithItsStoredCasing(string typed)
    {
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        var user = new User(null, "Ana.Admin@Platform.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.PlatformAdmin);
        await userRepository.AddAsync(user);

        var result = await CreateHandler(userRepository).HandleAsync(new LoginCommand(typed, "Sup3rSecret!"));

        Assert.Equal(user.Id, result?.UserId);
    }

    // Known compatibility limitation (ADR-010 decision 7): on a case-sensitive store an
    // existing mixed-case account is not found with a different casing.
    // SQL Server's collation does find it (SqlServerUserEmailCollationTests).
    [Fact]
    public async Task HandleAsync_MixedCaseAccount_DifferentCasing_IsNotFoundOnACaseSensitiveStore()
    {
        var passwordHasher = new PasswordHasher();
        var userRepository = new InMemoryUserRepository();
        await userRepository.AddAsync(new User(null, "Ana.Admin@Platform.test", passwordHasher.Hash("Sup3rSecret!"), UserRole.PlatformAdmin));

        Assert.Null(await CreateHandler(userRepository).HandleAsync(new LoginCommand("ana.admin@platform.test", "Sup3rSecret!")));
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

    // --- Patient record status ---------------------------------------------

    private static async Task<(InMemoryUserRepository Users, InMemoryPatientRepository Patients, Patient Patient)> SeedLinkedPatientAsync(
        bool patientActive)
    {
        var clinicId = Guid.NewGuid();
        var userRepository = new InMemoryUserRepository();
        var patientRepository = new InMemoryPatientRepository(userRepository);
        var user = new User(clinicId, "patient@clinic.test", new PasswordHasher().Hash("Sup3rSecret!"), UserRole.Patient);
        var patient = new Patient(clinicId, "Ana", "Petrovic", "+381600000000");
        patient.LinkUser(user.Id);

        if (!patientActive)
        {
            patient.Deactivate();
        }

        await userRepository.AddAsync(user);
        patientRepository.Seed(patient);

        return (userRepository, patientRepository, patient);
    }

    [Fact]
    public async Task HandleAsync_PatientWithActiveRecord_SignsIn()
    {
        var (users, patients, _) = await SeedLinkedPatientAsync(patientActive: true);

        var result = await CreateHandler(users, patients).HandleAsync(new LoginCommand("patient@clinic.test", "Sup3rSecret!"));

        Assert.NotNull(result);
        Assert.Equal(nameof(UserRole.Patient), result!.Role);
    }

    // A deactivated patient's still-active user account is refused, with the
    // same null as every other rejection.
    [Fact]
    public async Task HandleAsync_PatientWithDeactivatedRecord_ReturnsNull()
    {
        var (users, patients, _) = await SeedLinkedPatientAsync(patientActive: false);

        Assert.Null(await CreateHandler(users, patients).HandleAsync(new LoginCommand("patient@clinic.test", "Sup3rSecret!")));
    }

    // Reactivating the record restores access.
    [Fact]
    public async Task HandleAsync_PatientRecordReactivated_SignsInAgain()
    {
        var (users, patients, patient) = await SeedLinkedPatientAsync(patientActive: false);
        patient.Activate();

        Assert.NotNull(await CreateHandler(users, patients).HandleAsync(new LoginCommand("patient@clinic.test", "Sup3rSecret!")));
    }

    // The patient check never runs for a wrong password, so it reveals nothing.
    [Fact]
    public async Task HandleAsync_DeactivatedPatient_WrongPassword_ReturnsNull()
    {
        var (users, patients, _) = await SeedLinkedPatientAsync(patientActive: false);

        Assert.Null(await CreateHandler(users, patients).HandleAsync(new LoginCommand("patient@clinic.test", "WrongPassword!")));
    }

    // Admin roles have no Patient record; a deactivated patient of their clinic
    // does not affect them.
    [Fact]
    public async Task HandleAsync_ClinicAdmin_IsUnaffectedByPatientRecords()
    {
        var (users, patients, patient) = await SeedLinkedPatientAsync(patientActive: false);
        await users.AddAsync(new User(patient.ClinicId, "admin@clinic.test", new PasswordHasher().Hash("Sup3rSecret!"), UserRole.ClinicAdmin));

        Assert.NotNull(await CreateHandler(users, patients).HandleAsync(new LoginCommand("admin@clinic.test", "Sup3rSecret!")));
    }
}
