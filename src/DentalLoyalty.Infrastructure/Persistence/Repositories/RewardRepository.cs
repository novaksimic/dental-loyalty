using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.Infrastructure.Persistence.Repositories;

public sealed class RewardRepository : IRewardRepository
{
    private readonly DentalLoyaltyDbContext _context;

    public RewardRepository(DentalLoyaltyDbContext context)
    {
        _context = context;
    }

    public async Task<Reward?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Rewards
            .SingleOrDefaultAsync(
                r => r.Id == id && r.ClinicId == clinicId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Reward>> ListForTenantAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Rewards
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetRedeemedRewardIdsAsync(
        Guid loyaltyCycleId,
        CancellationToken cancellationToken = default)
    {
        var rewardIds = await _context.RewardRedemptions
            .AsNoTracking()
            .Where(rr => rr.LoyaltyCycleId == loyaltyCycleId)
            .Select(rr => rr.RewardId)
            .ToListAsync(cancellationToken);

        return rewardIds.ToHashSet();
    }
}
