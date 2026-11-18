using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Identity;

// ADR-010 decision 2: a ClinicAdmin creates the user account of a patient of
// their own clinic; the link starts the first loyalty cycle (BUSINESS.md §5.1).
public class CreatePatientUserHandlerTests
{
    private const string ValidPassword = "Sup3rSecret!pass";

    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly InMemoryUserRepository _userRepository = new();
    private readonly InMemoryPatientRepository _patientRepository;
    private readonly PasswordHasher _passwordHasher = new();

    public CreatePatientUserHandlerTests()
    {
        _patientRepository = new InMemoryPatientRepository(_userRepository);
    }

    private CreatePatientUserHandler CreateHandler(FakeCurrentTenant? tenant = null) =>
        new(_userRepository, _patientRepository, _passwordHasher, tenant ?? FakeCurrentTenant.ForClinic(_clinicId));

    private Patient SeedPatient(Guid? clinicId = null)
    {
        var patient = new Patient(clinicId ?? _clinicId, "Jane", "Doe", "+381601234567");
        _patientRepository.Seed(patient);
        return patient;
    }

    private static CreatePatientUserCommand Command(Guid patientId, string? email = "jane@patient.test", string? password = ValidPassword) =>
        new(patientId, email!, password!);

    private void AssertNothingCreated(Patient? patient = null, string email = "jane@patient.test")
    {
        Assert.Empty(_patientRepository.LinkedUsers);
        Assert.Empty(_patientRepository.LoyaltyCycles);
        Assert.Null(_userRepository.GetByEmailAsync(email).Result);

        if (patient is not null)
        {
            Assert.Null(patient.UserId);
        }
    }

    [Fact]
    public async Task LinksCreatedUser_ToThePatient()
    {
        var patient = SeedPatient();

        var result = await CreateHandler().HandleAsync(Command(patient.Id));

        Assert.Equal(CreatePatientUserStatus.Created, result.Status);
        Assert.Equal(result.UserId, patient.UserId);
    }

    // BUSINESS.md §5.1: the first cycle starts at the moment the account is
    // linked, saved in the same operation as the link.
    [Fact]
    public async Task CreatesPatientUser_OfThePatientsClinic_AndStartsTheFirstCycle()
    {
        var patient = SeedPatient();

        var result = await CreateHandler().HandleAsync(Command(patient.Id));

        var user = Assert.Single(_patientRepository.LinkedUsers);
        Assert.Equal(result.UserId, user.Id);
        Assert.Equal(_clinicId, user.ClinicId);
        Assert.Equal(UserRole.Patient, user.Role);
        Assert.True(user.IsActive);

        var cycle = Assert.Single(_patientRepository.LoyaltyCycles);
        Assert.Equal(patient.Id, cycle.PatientId);
        Assert.Equal(_clinicId, cycle.ClinicId);
        Assert.Equal(LoyaltyCycleStatus.Active, cycle.Status);
    }

    [Fact]
    public async Task StoresNormalizedEmail_AndHashedPassword()
    {
        var patient = SeedPatient();

        var result = await CreateHandler().HandleAsync(Command(patient.Id, "  Jane.Doe@Patient.TEST "));

        var user = Assert.Single(_patientRepository.LinkedUsers);
        Assert.Equal("jane.doe@patient.test", user.Email);
        Assert.Equal("jane.doe@patient.test", result.Email);
        Assert.NotEqual(ValidPassword, user.PasswordHash);
        Assert.DoesNotContain(ValidPassword, user.PasswordHash);
        Assert.True(_passwordHasher.Verify(ValidPassword, user.PasswordHash));
    }

    [Fact]
    public async Task UnknownPatient_IsRejected_AndCreatesNothing()
    {
        var result = await CreateHandler().HandleAsync(Command(Guid.NewGuid()));

        Assert.Equal(CreatePatientUserStatus.PatientNotFound, result.Status);
        AssertNothingCreated();
    }

    // The tenant boundary: a patient of another clinic is indistinguishable from
    // none, and is left untouched.
    [Fact]
    public async Task PatientOfAnotherClinic_IsNotFound_AndCreatesNothing()
    {
        var foreign = SeedPatient(Guid.NewGuid());

        var result = await CreateHandler().HandleAsync(Command(foreign.Id));

        Assert.Equal(CreatePatientUserStatus.PatientNotFound, result.Status);
        AssertNothingCreated(foreign);
    }

    // Fails closed: a caller without a trusted tenant (e.g. a PlatformAdmin
    // token) reaches no patient.
    [Fact]
    public async Task WithoutTenant_IsNotFound_AndCreatesNothing()
    {
        var patient = SeedPatient();

        var result = await CreateHandler(FakeCurrentTenant.NoTenant()).HandleAsync(Command(patient.Id));

        Assert.Equal(CreatePatientUserStatus.PatientNotFound, result.Status);
        AssertNothingCreated(patient);
    }

    [Fact]
    public async Task InactivePatient_IsRejected_AndCreatesNothing()
    {
        var patient = SeedPatient();
        patient.Deactivate();

        var result = await CreateHandler().HandleAsync(Command(patient.Id));

        Assert.Equal(CreatePatientUserStatus.PatientInactive, result.Status);
        AssertNothingCreated(patient);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("jane@localhost")]
    public async Task InvalidEmail_IsRejected_AndCreatesNothing(string? email)
    {
        var patient = SeedPatient();

        var result = await CreateHandler().HandleAsync(Command(patient.Id, email));

        Assert.Equal(CreatePatientUserStatus.InvalidEmail, result.Status);
        AssertNothingCreated(patient);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("JANE@patient.test")]
    public async Task InvalidPassword_IsRejected_AndCreatesNothing(string? password)
    {
        var patient = SeedPatient();

        var result = await CreateHandler().HandleAsync(Command(patient.Id, password: password));

        Assert.Equal(CreatePatientUserStatus.InvalidPassword, result.Status);
        AssertNothingCreated(patient);
    }

    // An existing link is never handed over to another User: the attempt is
    // rejected before any User is created, and the link and cycle stay as they are.
    [Fact]
    public async Task PatientAlreadyLinkedToAnotherUser_IsRejected_AndNothingChanges()
    {
        var existingUserId = Guid.NewGuid();
        var patient = SeedPatient();
        patient.LinkUser(existingUserId);
        var updatedAt = patient.UpdatedAt;

        var result = await CreateHandler().HandleAsync(Command(patient.Id, "intruder@patient.test"));

        Assert.Equal(CreatePatientUserStatus.AlreadyLinked, result.Status);
        Assert.Equal(existingUserId, patient.UserId);
        Assert.Equal(updatedAt, patient.UpdatedAt);
        Assert.Empty(_patientRepository.LinkedUsers);
        Assert.Empty(_patientRepository.LoyaltyCycles);
        Assert.Null(await _userRepository.GetByEmailAsync("intruder@patient.test"));
    }

    // Repeating a successful request creates no second User and no second cycle,
    // and leaves the link unchanged.
    [Fact]
    public async Task RepeatedAfterSuccess_IsRejected_AndNothingChanges()
    {
        var patient = SeedPatient();
        var handler = CreateHandler();

        var first = await handler.HandleAsync(Command(patient.Id));
        var cycle = Assert.Single(_patientRepository.LoyaltyCycles);

        var second = await handler.HandleAsync(Command(patient.Id));

        Assert.Equal(CreatePatientUserStatus.AlreadyLinked, second.Status);
        Assert.Equal(first.UserId, patient.UserId);
        Assert.Equal(first.UserId, Assert.Single(_patientRepository.LinkedUsers).Id);
        Assert.Same(cycle, Assert.Single(_patientRepository.LoyaltyCycles));
    }

    [Theory]
    [InlineData("jane@patient.test")]
    [InlineData("JANE@Patient.Test")]
    public async Task EmailAlreadyInUse_InAnyCasing_LeavesPatientUnlinked(string email)
    {
        var patient = SeedPatient();
        await _userRepository.AddAsync(new User(Guid.NewGuid(), "jane@patient.test", "existing-hash", UserRole.ClinicAdmin));

        var result = await CreateHandler().HandleAsync(Command(patient.Id, email));

        Assert.Equal(CreatePatientUserStatus.EmailAlreadyExists, result.Status);
        Assert.Null(patient.UserId);
        Assert.Empty(_patientRepository.LinkedUsers);
        Assert.Empty(_patientRepository.LoyaltyCycles);
    }
}
