using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

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
}