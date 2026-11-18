using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public UserRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        await _context.Users.AddAsync(
            user,
            cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Recognised without provider-specific error codes, as for
            // referrals and service occurrences: after the failure, ask
            // whether another user now has this email (the unique index on
            // Users.Email). The single insert was rolled back, so nothing of
            // this user was saved.
            var taken = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Email == user.Email && u.Id != user.Id, cancellationToken);

            if (taken)
            {
                throw new DuplicateEmailException(
                    "A user with this email already exists. Nothing was saved.",
                    ex);
            }

            throw;
        }
    }

    // The email as given, or its lower-cased form (the form new accounts are
    // stored in, ADR-010 decision 4); an exact match is preferred. Both are
    // equality predicates, so the unique index on Users.Email is used - the
    // column is never wrapped in LOWER(). On SQL Server the default
    // case-insensitive collation matches both to the same single row, as
    // before.
    //
    // Known compatibility limitation, not the intended long-term behaviour: on
    // a case-sensitive provider (SQLite, EF in-memory), an existing account
    // stored in mixed case is found only when the input has the same casing.
    // Removing it needs the stored emails normalized by a data migration.
    public async Task<User?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        var lowered = email.ToLowerInvariant();

        return await _context.Users
            .Where(u => u.Email == email || u.Email == lowered)
            .OrderByDescending(u => u.Email == email)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
