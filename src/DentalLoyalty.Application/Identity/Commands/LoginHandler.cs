using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Identity.Commands;

public sealed class LoginHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    // Returns null for every rejection reason (unknown email, wrong password,
    // inactive user) so the caller cannot distinguish "no such user" from
    // "wrong password" from the response alone.
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
