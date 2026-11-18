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

    public async Task<IReadOnlyList<Reward>> ListDefinitionsForTenantAsync(
        Guid clinicId,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var rewards = _context.Rewards
            .AsNoTracking()
            .Where(r => r.ClinicId == clinicId);

        if (isActive is bool active)
        {
            rewards = rewards.Where(r => r.IsActive == active);
        }

        return await rewards
            .OrderBy(r => r.RequiredPoints)
            .ThenBy(r => r.RequiredReferrals)
            .ThenBy(r => r.Name)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        Reward reward,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reward);

        await _context.Rewards.AddAsync(reward, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        Reward reward,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reward);

        _context.Rewards.Update(reward);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
