using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class ClinicRepository : IClinicRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public ClinicRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        Clinic clinic,
        LoyaltyConfiguration loyaltyConfiguration,
        CancellationToken cancellationToken = default)
    {
        await _context.Clinics.AddAsync(
            clinic,
            cancellationToken);

        await _context.LoyaltyConfigurations.AddAsync(
            loyaltyConfiguration,
            cancellationToken);

        // One SaveChanges = one database transaction for both rows.
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.Clinics
            .AsNoTracking()
            .AnyAsync(c => c.Id == id, cancellationToken);
    }
}