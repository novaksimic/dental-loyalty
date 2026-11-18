using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.TestDoubles;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly List<User> _users = new();

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        // Same rule as UserRepository: exact, or the lower-cased form;
        // exact preferred. Case-sensitive, like SQLite.
        var lowered = email.ToLowerInvariant();
        var user = _users.FirstOrDefault(u => u.Email == email)
            ?? _users.FirstOrDefault(u => u.Email == lowered);
        return Task.FromResult(user);
    }
}
