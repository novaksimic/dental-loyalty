using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Identity.Commands;

public sealed class CreatePlatformAdminHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public CreatePlatformAdminHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<Guid> HandleAsync(
        CreatePlatformAdminCommand command,
        CancellationToken cancellationToken = default)
    {
        var existingUser = await _userRepository.GetByEmailAsync(
            command.Email,
            cancellationToken);

        if (existingUser is not null)
        {
            throw new InvalidOperationException(
                "A user with this email already exists.");
        }

        var passwordHash = _passwordHasher.Hash(command.Password);

        var user = new User(
            clinicId: null,
            email: command.Email,
            passwordHash: passwordHash,
            role: UserRole.PlatformAdmin);

        await _userRepository.AddAsync(user, cancellationToken);

        return user.Id;
    }
}
