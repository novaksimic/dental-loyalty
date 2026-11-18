using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Identity;

// ADR-010 decision 1: a PlatformAdmin provisions a ClinicAdmin for an existing
// clinic, with a normalized email and a password that satisfies PasswordPolicy.
public class CreateClinicAdminHandlerTests
{
    private const string ValidPassword = "Sup3rSecret!pass";

    private readonly InMemoryUserRepository _userRepository = new();
    private readonly InMemoryClinicRepository _clinicRepository = new();
    private readonly PasswordHasher _passwordHasher = new();

    private CreateClinicAdminHandler CreateHandler() =>
        new(_userRepository, _clinicRepository, _passwordHasher);

    private async Task<Guid> SeedClinicAsync()
    {
        var clinic = new Clinic("Bright Smiles Dental");
        await _clinicRepository.AddAsync(clinic, LoyaltyConfiguration.CreateDefault(clinic.Id));
        return clinic.Id;
    }

    [Fact]
    public async Task CreatesClinicAdmin_ForGivenClinic()
    {
        var clinicId = await SeedClinicAsync();

        var result = await CreateHandler().HandleAsync(
            new CreateClinicAdminCommand(clinicId, "admin@clinic.test", ValidPassword));

        Assert.Equal(CreateClinicAdminStatus.Created, result.Status);

        var created = await _userRepository.GetByEmailAsync("admin@clinic.test");

        Assert.NotNull(created);
        Assert.Equal(result.UserId, created!.Id);
        Assert.Equal(clinicId, created.ClinicId);
        Assert.Equal(UserRole.ClinicAdmin, created.Role);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task StoresNormalizedEmail()
    {
        var clinicId = await SeedClinicAsync();

        var result = await CreateHandler().HandleAsync(
            new CreateClinicAdminCommand(clinicId, "  Ana.Admin@Clinic.TEST  ", ValidPassword));

        Assert.Equal(CreateClinicAdminStatus.Created, result.Status);
        Assert.Equal("ana.admin@clinic.test", result.Email);
        Assert.Equal("ana.admin@clinic.test", (await _userRepository.GetByEmailAsync("ana.admin@clinic.test"))!.Email);
    }

    [Fact]
    public async Task HashesPassword_NeverStoresPlaintext()
    {
        var clinicId = await SeedClinicAsync();

        await CreateHandler().HandleAsync(new CreateClinicAdminCommand(clinicId, "admin@clinic.test", ValidPassword));

        var created = (await _userRepository.GetByEmailAsync("admin@clinic.test"))!;

        Assert.NotEqual(ValidPassword, created.PasswordHash);
        Assert.DoesNotContain(ValidPassword, created.PasswordHash);
        Assert.True(_passwordHasher.Verify(ValidPassword, created.PasswordHash));
    }

    [Fact]
    public async Task UnknownClinic_IsRejected_AndCreatesNothing()
    {
        await SeedClinicAsync();

        var result = await CreateHandler().HandleAsync(
            new CreateClinicAdminCommand(Guid.NewGuid(), "admin@clinic.test", ValidPassword));

        Assert.Equal(CreateClinicAdminStatus.ClinicNotFound, result.Status);
        Assert.Null(result.UserId);
        Assert.Null(await _userRepository.GetByEmailAsync("admin@clinic.test"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("admin@localhost")]
    [InlineData("Admin <admin@clinic.test>")]
    public async Task InvalidEmail_IsRejected(string? email)
    {
        var clinicId = await SeedClinicAsync();

        var result = await CreateHandler().HandleAsync(new CreateClinicAdminCommand(clinicId, email!, ValidPassword));

        Assert.Equal(CreateClinicAdminStatus.InvalidEmail, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("123456789")]
    [InlineData("ADMIN@clinic.test")]
    public async Task InvalidPassword_IsRejected_AndCreatesNothing(string? password)
    {
        var clinicId = await SeedClinicAsync();

        var result = await CreateHandler().HandleAsync(new CreateClinicAdminCommand(clinicId, "admin@clinic.test", password!));

        Assert.Equal(CreateClinicAdminStatus.InvalidPassword, result.Status);
        Assert.Null(await _userRepository.GetByEmailAsync("admin@clinic.test"));
    }

    [Fact]
    public async Task DuplicateEmail_InAnyCasing_IsRejected()
    {
        var clinicId = await SeedClinicAsync();
        var handler = CreateHandler();

        await handler.HandleAsync(new CreateClinicAdminCommand(clinicId, "admin@clinic.test", ValidPassword));

        var result = await handler.HandleAsync(new CreateClinicAdminCommand(clinicId, "ADMIN@Clinic.Test", ValidPassword));

        Assert.Equal(CreateClinicAdminStatus.EmailAlreadyExists, result.Status);
    }

    // Users.Email is unique platform-wide: an email used by a user of another
    // clinic or another role cannot be reused.
    [Fact]
    public async Task EmailUsedByAnotherClinicsUser_IsRejected()
    {
        var clinicId = await SeedClinicAsync();
        await _userRepository.AddAsync(new User(Guid.NewGuid(), "admin@clinic.test", "hash", UserRole.Patient));

        var result = await CreateHandler().HandleAsync(new CreateClinicAdminCommand(clinicId, "admin@clinic.test", ValidPassword));

        Assert.Equal(CreateClinicAdminStatus.EmailAlreadyExists, result.Status);
    }
}
