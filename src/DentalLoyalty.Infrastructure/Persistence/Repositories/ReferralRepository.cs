using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class ReferralRepository : IReferralRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public ReferralRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task<Referral?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Referrals
            .SingleOrDefaultAsync(
                r => r.Id == id && r.ClinicId == clinicId,
                cancellationToken);
    }

    public async Task<bool> ExistsForReferredPatientAsync(
        Guid referredPatientId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Referrals
            .AsNoTracking()
            .AnyAsync(
                r => r.ReferredPatientId == referredPatientId && r.Status != ReferralStatus.Cancelled,
                cancellationToken);
    }

    public async Task AddAsync(
        Referral referral,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referral);

        await _context.Referrals.AddAsync(referral, cancellationToken);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Recognised without provider-specific error codes, as for service
            // occurrences: after the failure, ask whether the patient is now
            // referred by another referral.
            var referred = await _context.Referrals
                .AsNoTracking()
                .AnyAsync(
                    r => r.ReferredPatientId == referral.ReferredPatientId
                        && r.Id != referral.Id
                        && r.Status != ReferralStatus.Cancelled,
                    cancellationToken);

            if (referred)
            {
                throw new DuplicateReferralException(
                    "The referred patient has already been referred. Nothing was saved.",
                    ex);
            }

            throw;
        }
    }
}
