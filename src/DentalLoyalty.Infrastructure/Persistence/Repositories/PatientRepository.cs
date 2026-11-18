using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class PatientRepository : IPatientRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public PatientRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        Patient patient,
        LoyaltyAccount loyaltyAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(loyaltyAccount);

        // ADR-006 §3: the account belongs to this Patient, in the Patient's clinic.
        if (loyaltyAccount.PatientId != patient.Id || loyaltyAccount.ClinicId != patient.ClinicId)
        {
            throw new InvalidOperationException(
                "The loyalty account must belong to the patient being created and its clinic.");
        }

        await _context.Patients.AddAsync(
            patient,
            cancellationToken);

        await _context.LoyaltyAccounts.AddAsync(
            loyaltyAccount,
            cancellationToken);

        // One SaveChanges = one database transaction for both rows (ADR-006 §2).
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task LinkUserAsync(
        Patient patient,
        User user,
        LoyaltyCycle firstCycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(firstCycle);

        // The Patient must come from this unit of work, so the link is saved as
        // an update of the row that was read rather than a blind overwrite.
        var entry = _context.Entry(patient);

        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "The patient must be loaded through this repository before it is linked.");
        }

        // An existing link is never replaced, and a Patient that was already
        // linked when it was read gets no further User or cycle.
        if (entry.Property(p => p.UserId).OriginalValue is not null)
        {
            throw new InvalidOperationException(
                "The patient is already linked to a user account.");
        }

        if (patient.UserId != user.Id || user.ClinicId != patient.ClinicId)
        {
            throw new InvalidOperationException(
                "The patient must be linked to the given user, in the same clinic.");
        }

        if (firstCycle.PatientId != patient.Id || firstCycle.ClinicId != patient.ClinicId)
        {
            throw new InvalidOperationException(
                "The loyalty cycle must belong to the linked patient and its clinic.");
        }

        await _context.Users.AddAsync(
            user,
            cancellationToken);

        await _context.LoyaltyCycles.AddAsync(
            firstCycle,
            cancellationToken);

        // One SaveChanges = one database transaction for the new User, the
        // Patient's link and the cycle the link starts. If another operation
        // linked the same Patient after it was read here, its active cycle
        // makes this cycle's insert fail on the one-active-cycle-per-Patient
        // index, and the whole save - including this link - is rolled back.
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Recognised without provider-specific error codes, as for
            // referrals and service occurrences: after the rollback, ask which
            // concurrent change won.
            if (await _context.Users.AsNoTracking()
                    .AnyAsync(u => u.Email == user.Email && u.Id != user.Id, cancellationToken))
            {
                throw new DuplicateEmailException(
                    "A user with this email already exists. Nothing was saved.",
                    ex);
            }

            if (await _context.LoyaltyCycles.AsNoTracking()
                    .AnyAsync(c => c.PatientId == patient.Id && c.Id != firstCycle.Id, cancellationToken))
            {
                throw new ConcurrencyConflictException(
                    "The patient was linked to a user account by another operation. Nothing was saved.",
                    ex);
            }

            throw;
        }
    }

    public async Task<Patient?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .SingleOrDefaultAsync(
                p => p.Id == id,
                cancellationToken);
    }

    public async Task<Patient?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .SingleOrDefaultAsync(
                p => p.Id == id && p.ClinicId == clinicId,
                cancellationToken);
    }

    public async Task<Patient?> GetByIdForPatientOwnerAsync(
        Guid id,
        Guid clinicId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .SingleOrDefaultAsync(
                p => p.Id == id && p.ClinicId == clinicId && p.UserId == userId,
                cancellationToken);
    }

    public async Task<Patient?> GetByUserIdForTenantAsync(
        Guid userId,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Patients
            .SingleOrDefaultAsync(
                p => p.UserId == userId && p.ClinicId == clinicId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        Patient patient,
        CancellationToken cancellationToken = default)
    {
        _context.Patients.Update(patient);

        await _context.SaveChangesAsync(cancellationToken);
    }

    // The escape character for LIKE patterns built from search terms.
    private const string LikeEscape = "\\";

    public async Task<PatientPage> ListForTenantAsync(
        Guid clinicId,
        IReadOnlyList<string> searchTerms,
        bool? isActive,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchTerms);

        // The tenant filter is part of the query itself, before anything else.
        var patients = _context.Patients
            .AsNoTracking()
            .Where(p => p.ClinicId == clinicId);

        if (isActive is bool active)
        {
            patients = patients.Where(p => p.IsActive == active);
        }

        // LIKE '%term%' per term; each term is a parameter, never SQL text. Its
        // own %, _ and [ are escaped so they match literally. Case sensitivity
        // follows the column collation (case-insensitive on SQL Server's
        // default collation).
        foreach (var term in searchTerms)
        {
            var pattern = $"%{EscapeLike(term)}%";

            patients = patients.Where(p =>
                EF.Functions.Like(p.FirstName, pattern, LikeEscape)
                || EF.Functions.Like(p.LastName, pattern, LikeEscape)
                || (p.Email != null && EF.Functions.Like(p.Email, pattern, LikeEscape))
                || EF.Functions.Like(p.PhoneNumber, pattern, LikeEscape));
        }

        var totalCount = await patients.CountAsync(cancellationToken);

        var items = await patients
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new PatientPage(items, totalCount);
    }

    private static string EscapeLike(string term) =>
        term
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
            .Replace("[", LikeEscape + "[", StringComparison.Ordinal);
}