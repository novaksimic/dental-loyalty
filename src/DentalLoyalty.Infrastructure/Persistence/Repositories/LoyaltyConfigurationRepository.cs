using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class LoyaltyConfigurationRepository : ILoyaltyConfigurationRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public LoyaltyConfigurationRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task<LoyaltyConfiguration?> GetByClinicIdAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.LoyaltyConfigurations
            .SingleOrDefaultAsync(
                lc => lc.ClinicId == clinicId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        LoyaltyConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        _context.LoyaltyConfigurations.Update(configuration);

        await _context.SaveChangesAsync(cancellationToken);
    }
}
