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
}
