using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Identity.Commands;

public sealed class CreatePatientUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPasswordHasher _passwordHasher;

    public CreatePatientUserHandler(
        IUserRepository userRepository,
        IPatientRepository patientRepository,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _patientRepository = patientRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<Guid> HandleAsync(
        CreatePatientUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var patient = await _patientRepository.GetByIdAsync(
            command.PatientId,
            cancellationToken);

        if (patient is null)
        {
            throw new InvalidOperationException(
                "Patient does not exist.");
        }

        // The link is never replaced: a linked Patient - whether a repeated request
        // for its own User or an attempt by another User - is rejected before
        // anything is created, so its UserId and its loyalty cycle stay as they are.
        if (patient.UserId is not null)
        {
            throw new InvalidOperationException(
                "Patient is already linked to a user account.");
        }

        var existingUser = await _userRepository.GetByEmailAsync(
            command.Email,
            cancellationToken);

        if (existingUser is not null)
        {
            throw new InvalidOperationException(
                "A user with this email already exists.");
        }

        var passwordHash = _passwordHasher.Hash(command.Password);

        // ClinicId is taken from the existing Patient, not from the caller,
        // so the User's tenant always matches the Patient it is linked to.
        var user = new User(
            clinicId: patient.ClinicId,
            email: command.Email,
            passwordHash: passwordHash,
            role: UserRole.Patient);

        // BUSINESS.md §5.1: the first loyalty cycle starts when registration and
        // account linking are successfully completed.
        patient.LinkUser(user.Id);

        var firstCycle = LoyaltyCycle.StartFirst(patient);

        // One save for the User, the link and the cycle: the cycle never starts
        // without a completed link, and a failed link leaves no User behind.
        await _patientRepository.LinkUserAsync(
            patient,
            user,
            firstCycle,
            cancellationToken);

        return user.Id;
    }
}
