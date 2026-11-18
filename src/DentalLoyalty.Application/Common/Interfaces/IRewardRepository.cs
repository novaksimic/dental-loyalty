using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

// The clinic's reward definitions (BUSINESS.md §10, §22) and their redemptions.
// clinicId must come from the trusted tenant context (ICurrentTenant), never from
// the request.
public interface IRewardRepository
{
    // Returns the reward only if it belongs to clinicId - a reward of another
    // clinic is indistinguishable from none.
    Task<Reward?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // Every reward of the clinic, active or not.
    Task<IReadOnlyList<Reward>> ListForTenantAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // The ids of the rewards redeemed in the given loyalty cycle.
    Task<IReadOnlySet<Guid>> GetRedeemedRewardIdsAsync(
        Guid loyaltyCycleId,
        CancellationToken cancellationToken = default);

    // The clinic's reward definitions for administration, in milestone order
    // (required points, then required referrals, then name - as the patient's
    // rewards are shown); only those with the given status when isActive is set.
    Task<IReadOnlyList<Reward>> ListDefinitionsForTenantAsync(
        Guid clinicId,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Reward reward,
        CancellationToken cancellationToken = default);

    // Saves changes to a reward loaded through GetByIdForTenantAsync.
    Task UpdateAsync(
        Reward reward,
        CancellationToken cancellationToken = default);
}
