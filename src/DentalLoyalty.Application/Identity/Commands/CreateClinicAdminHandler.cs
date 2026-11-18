using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Identity.Commands;

// Creates a ClinicAdmin account for an existing clinic (ADR-010 decision 1).
// Only a PlatformAdmin may call it - enforced by the API - so ClinicId comes from
// the request: a PlatformAdmin has no tenant of its own and names the clinic it
// provisions. ClinicAdmin accounts are never self-registered.
//
// Validation runs before anything is written, cheapest first; a rejected request
// creates nothing. The email is stored in its normalized form (EmailAddress), so
// the duplicate check and later logins compare like with like.
//
// Throws DuplicateEmailException when a concurrent request created a user with
// the same email after the duplicate check; nothing is saved in that case.
public sealed class CreateClinicAdminHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IPasswordHasher _passwordHasher;

    public CreateClinicAdminHandler(
        IUserRepository userRepository,
        IClinicRepository clinicRepository,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _clinicRepository = clinicRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<CreateClinicAdminResult> HandleAsync(
        CreateClinicAdminCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!EmailAddress.TryNormalize(command.Email, out var email))
        {
            return CreateClinicAdminResult.Rejected(CreateClinicAdminStatus.InvalidEmail);
        }

        if (!PasswordPolicy.IsSatisfiedBy(command.Password, email))
        {
            return CreateClinicAdminResult.Rejected(CreateClinicAdminStatus.InvalidPassword);
        }

        if (!await _clinicRepository.ExistsAsync(command.ClinicId, cancellationToken))
        {
            return CreateClinicAdminResult.Rejected(CreateClinicAdminStatus.ClinicNotFound);
        }

        // Users.Email is unique platform-wide, whatever the role or clinic. The
        // unique index still stops a request that races with this check.
        if (await _userRepository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            return CreateClinicAdminResult.Rejected(CreateClinicAdminStatus.EmailAlreadyExists);
        }

        var user = new User(
            clinicId: command.ClinicId,
            email: email,
            passwordHash: _passwordHasher.Hash(command.Password),
            role: UserRole.ClinicAdmin);

        await _userRepository.AddAsync(user, cancellationToken);

        return new CreateClinicAdminResult(CreateClinicAdminStatus.Created, user.Id, user.Email);
    }
}
