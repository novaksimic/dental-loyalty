using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Identity;

public class CreatePatientUserHandlerTests
{
    [Fact]
    public async Task HandleAsync_LinksCreatedUser_ToExistingPatient()
    {
        var clinicId = Guid.NewGuid();
        var patient = new Patient(clinicId, "Jane", "Doe", "+381601234567");

        var patientRepository = new InMemoryPatientRepository();
        patientRepository.Seed(patient);

        var userRepository = new InMemoryUserRepository();
        var passwordHasher = new PasswordHasher();

        var handler = new CreatePatientUserHandler(
            userRepository,
            patientRepository,
            passwordHasher);

        var userId = await handler.HandleAsync(
            new CreatePatientUserCommand(patient.Id, "jane@patient.test", "Sup3rSecret!"));

        var updatedPatient = await patientRepository.GetByIdAsync(patient.Id);

        Assert.NotNull(updatedPatient);
        Assert.Equal(userId, updatedPatient!.UserId);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownPatient_ThrowsInvalidOperationException()
    {
        var handler = new CreatePatientUserHandler(
            new InMemoryUserRepository(),
            new InMemoryPatientRepository(),
            new PasswordHasher());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new CreatePatientUserCommand(Guid.NewGuid(), "jane@patient.test", "Sup3rSecret!")));
    }

    // BUSINESS.md §5.1: the first cycle starts at the moment registration and
    // account linking are completed, saved in the same operation as the link.
    [Fact]
    public async Task HandleAsync_FirstLink_StartsTheFirstLoyaltyCycle_WithTheUserAndLink()
    {
        var clinicId = Guid.NewGuid();
        var patient = new Patient(clinicId, "Jane", "Doe", "+381601234567");

        var userRepository = new InMemoryUserRepository();
        var patientRepository = new InMemoryPatientRepository(userRepository);
        patientRepository.Seed(patient);

        var handler = new CreatePatientUserHandler(userRepository, patientRepository, new PasswordHasher());

        var userId = await handler.HandleAsync(
            new CreatePatientUserCommand(patient.Id, "jane@patient.test", "Sup3rSecret!"));

        var user = Assert.Single(patientRepository.LinkedUsers);
        Assert.Equal(userId, user.Id);
        Assert.Equal(clinicId, user.ClinicId);
        Assert.Equal(UserRole.Patient, user.Role);

        var cycle = Assert.Single(patientRepository.LoyaltyCycles);
        Assert.Equal(patient.Id, cycle.PatientId);
        Assert.Equal(clinicId, cycle.ClinicId);
        Assert.Equal(LoyaltyCycleStatus.Active, cycle.Status);
    }

    // An existing link is never handed over to another User: the attempt is
    // rejected before any User is created, and the link and cycle stay as they are.
    [Fact]
    public async Task HandleAsync_PatientAlreadyLinkedToAnotherUser_IsRejected_AndNothingChanges()
    {
        var clinicId = Guid.NewGuid();
        var existingUserId = Guid.NewGuid();
        var patient = new Patient(clinicId, "Jane", "Doe", "+381601234567");
        patient.LinkUser(existingUserId);
        var updatedAt = patient.UpdatedAt;

        var userRepository = new InMemoryUserRepository();
        var patientRepository = new InMemoryPatientRepository(userRepository);
        patientRepository.Seed(patient);

        var handler = new CreatePatientUserHandler(userRepository, patientRepository, new PasswordHasher());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new CreatePatientUserCommand(patient.Id, "intruder@patient.test", "Sup3rSecret!")));

        Assert.Equal(existingUserId, patient.UserId);
        Assert.Equal(updatedAt, patient.UpdatedAt);
        Assert.Empty(patientRepository.LinkedUsers);
        Assert.Empty(patientRepository.LoyaltyCycles);
        Assert.Null(await userRepository.GetByEmailAsync("intruder@patient.test"));
    }

    // Repeating a successful registration for the same User creates no second
    // User and no second cycle, and leaves the link unchanged.
    [Fact]
    public async Task HandleAsync_RepeatedForTheSameUserAfterSuccess_IsRejected_AndNothingChanges()
    {
        var clinicId = Guid.NewGuid();
        var patient = new Patient(clinicId, "Jane", "Doe", "+381601234567");

        var userRepository = new InMemoryUserRepository();
        var patientRepository = new InMemoryPatientRepository(userRepository);
        patientRepository.Seed(patient);

        var handler = new CreatePatientUserHandler(userRepository, patientRepository, new PasswordHasher());
        var command = new CreatePatientUserCommand(patient.Id, "jane@patient.test", "Sup3rSecret!");

        var userId = await handler.HandleAsync(command);
        var cycle = Assert.Single(patientRepository.LoyaltyCycles);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(command));

        Assert.Equal(userId, patient.UserId);
        Assert.Equal(userId, Assert.Single(patientRepository.LinkedUsers).Id);
        Assert.Same(cycle, Assert.Single(patientRepository.LoyaltyCycles));
    }

    [Fact]
    public async Task HandleAsync_WithUnknownPatient_SavesNoUserAndStartsNoCycle()
    {
        var userRepository = new InMemoryUserRepository();
        var patientRepository = new InMemoryPatientRepository(userRepository);

        var handler = new CreatePatientUserHandler(userRepository, patientRepository, new PasswordHasher());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new CreatePatientUserCommand(Guid.NewGuid(), "jane@patient.test", "Sup3rSecret!")));

        Assert.Empty(patientRepository.LinkedUsers);
        Assert.Empty(patientRepository.LoyaltyCycles);
        Assert.Null(await userRepository.GetByEmailAsync("jane@patient.test"));
    }

    [Fact]
    public async Task HandleAsync_WithEmailAlreadyInUse_LeavesPatientUnlinkedAndStartsNoCycle()
    {
        var clinicId = Guid.NewGuid();
        var patient = new Patient(clinicId, "Jane", "Doe", "+381601234567");

        var userRepository = new InMemoryUserRepository();
        await userRepository.AddAsync(new User(clinicId, "jane@patient.test", "existing-hash", UserRole.Patient));

        var patientRepository = new InMemoryPatientRepository(userRepository);
        patientRepository.Seed(patient);

        var handler = new CreatePatientUserHandler(userRepository, patientRepository, new PasswordHasher());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(
                new CreatePatientUserCommand(patient.Id, "jane@patient.test", "Sup3rSecret!")));

        Assert.Null(patient.UserId);
        Assert.Empty(patientRepository.LinkedUsers);
        Assert.Empty(patientRepository.LoyaltyCycles);
    }
}
