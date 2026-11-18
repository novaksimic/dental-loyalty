using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Identity.Commands;

public sealed class LoginHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IPatientRepository _patientRepository;

    public LoginHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IPatientRepository patientRepository)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _patientRepository = patientRepository;
    }

    // Returns null for every rejection reason (unknown email, wrong password,
    // inactive user, deactivated patient) so the caller cannot distinguish "no
    // such user" from "wrong password" from the response alone.
    public async Task<LoginResponse?> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken = default)
    {
        var email = command.Email?.Trim() ?? string.Empty;

        if (email.Length == 0 || string.IsNullOrEmpty(command.Password))
        {
            return null;
        }

        var user = await _userRepository.GetByEmailAsync(
            email,
            cancellationToken);

        if (user is null)
        {
            return null;
        }

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return null;
        }

        if (!user.IsActive)
        {
            return null;
        }

        // A patient's access follows their Patient record: deactivating the
        // patient (DELETE /api/patients/{id}) deactivates only that record, so
        // the linked account is refused here. Checked after the password, like
        // the user's own status, so it reveals nothing to someone without it.
        // Other roles have no Patient record and are unaffected.
        if (user.Role == UserRole.Patient && user.ClinicId is Guid clinicId)
        {
            var patient = await _patientRepository.GetByUserIdForTenantAsync(
                user.Id,
                clinicId,
                cancellationToken);

            if (patient is { IsActive: false })
            {
                return null;
            }
        }

        var token = _jwtTokenGenerator.GenerateToken(
            user.Id,
            user.Role,
            user.ClinicId);

        return new LoginResponse(
            Token: token,
            UserId: user.Id,
            Role: user.Role.ToString(),
            ClinicId: user.ClinicId);
    }
}
