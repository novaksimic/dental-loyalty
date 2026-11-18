using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

public interface IUserRepository
{
    // Throws DuplicateEmailException when another user already has the email
    // (Users.Email is unique); nothing is saved in that case.
    Task AddAsync(
        User user,
        CancellationToken cancellationToken = default);

    // The user whose email is exactly email or its lower-cased form, preferring
    // the exact match. Used for login and for duplicate-email checks. On a
    // case-sensitive database an existing mixed-case email is found only when
    // typed with the same casing - a known compatibility limitation (ADR-010 decision 7).
    Task<User?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default);
}
