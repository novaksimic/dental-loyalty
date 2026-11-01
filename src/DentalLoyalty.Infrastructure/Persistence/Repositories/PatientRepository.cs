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
        await _context.SaveChangesAsync(cancellationToken);
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
}