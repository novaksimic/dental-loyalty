using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Identity.Commands;

// Creates the user account of a patient of the caller's own clinic and links it
// to the patient's record (ADR-010 decision 2). Patients do not self-register:
// a ClinicAdmin creates the account. The link starts the patient's first loyalty
// cycle (BUSINESS.md §5.1).
//
// The clinic is the authenticated tenant (ICurrentTenant), never a value from
// the request: the patient is loaded within it, so a patient of another clinic
// is indistinguishable from none, and the User gets the patient's clinic.
//
// Validation runs before anything is written; a rejected request creates
// nothing. The User, the patient's link and the first cycle are saved together
// in one save (IPatientRepository.LinkUserAsync) - all or nothing.
//
// Throws DuplicateEmailException when a concurrent request took the email, and
// ConcurrencyConflictException when the patient was linked concurrently, after
// the checks here; nothing is saved in either case.
public sealed class CreatePatientUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentTenant _currentTenant;

    public CreatePatientUserHandler(
        IUserRepository userRepository,
        IPatientRepository patientRepository,
        IPasswordHasher passwordHasher,
        ICurrentTenant currentTenant)
    {
        _userRepository = userRepository;
        _patientRepository = patientRepository;
        _passwordHasher = passwordHasher;
        _currentTenant = currentTenant;
    }

    public async Task<CreatePatientUserResult> HandleAsync(
        CreatePatientUserCommand command,
        CancellationToken cancellationToken = default)
    {
        // Fails closed like the other tenant-scoped patient use cases: without a
        // trusted tenant there is no patient this caller can reach.
        if (!_currentTenant.HasTenant)
        {
            return CreatePatientUserResult.Rejected(CreatePatientUserStatus.PatientNotFound);
        }

        if (!EmailAddress.TryNormalize(command.Email, out var email))
        {
            return CreatePatientUserResult.Rejected(CreatePatientUserStatus.InvalidEmail);
        }

        if (!PasswordPolicy.IsSatisfiedBy(command.Password, email))
        {
            return CreatePatientUserResult.Rejected(CreatePatientUserStatus.InvalidPassword);
        }

        var patient = await _patientRepository.GetByIdForTenantAsync(
            command.PatientId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        if (patient is null)
        {
            return CreatePatientUserResult.Rejected(CreatePatientUserStatus.PatientNotFound);
        }

        if (!patient.IsActive)
        {
            return CreatePatientUserResult.Rejected(CreatePatientUserStatus.PatientInactive);
        }

        // The link is never replaced: a linked Patient - whether a repeated request
        // for its own User or an attempt by another User - is rejected before
        // anything is created, so its UserId and its loyalty cycle stay as they are.
        if (patient.UserId is not null)
        {
            return CreatePatientUserResult.Rejected(CreatePatientUserStatus.AlreadyLinked);
        }

        // Users.Email is unique platform-wide, whatever the role or clinic. The
        // unique index still stops a request that races with this check.
        if (await _userRepository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            return CreatePatientUserResult.Rejected(CreatePatientUserStatus.EmailAlreadyExists);
        }

        // ClinicId is taken from the Patient - itself loaded within the caller's
        // tenant - so the User's tenant always matches the Patient it is linked to.
        var user = new User(
            clinicId: patient.ClinicId,
            email: email,
            passwordHash: _passwordHasher.Hash(command.Password),
            role: UserRole.Patient);

        // BUSINESS.md §5.1: the first loyalty cycle starts when the account is
        // linked to the Patient record.
        patient.LinkUser(user.Id);

        var firstCycle = LoyaltyCycle.StartFirst(patient);

        // One save for the User, the link and the cycle: the cycle never starts
        // without a completed link, and a failed link leaves no User behind.
        await _patientRepository.LinkUserAsync(
            patient,
            user,
            firstCycle,
            cancellationToken);

        return new CreatePatientUserResult(CreatePatientUserStatus.Created, user.Id, user.Email);
    }
}
