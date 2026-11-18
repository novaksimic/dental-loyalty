using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Rewards.Queries;

// The caller's own clinic's reward definitions, in milestone order (BUSINESS.md
// §9, §18). Only the authenticated tenant's rewards are ever read. Returns null
// when the caller has no trusted tenant.
public sealed class GetRewardsHandler
{
    private readonly IRewardRepository _rewardRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetRewardsHandler(
        IRewardRepository rewardRepository,
        ICurrentTenant currentTenant)
    {
        _rewardRepository = rewardRepository;
        _currentTenant = currentTenant;
    }

    public async Task<IReadOnlyList<RewardDefinitionDto>?> HandleAsync(
        GetRewardsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        var rewards = await _rewardRepository.ListDefinitionsForTenantAsync(
            _currentTenant.ClinicId!.Value,
            query.IsActive,
            cancellationToken);

        return rewards.Select(RewardDefinitionDto.From).ToList();
    }
}

// One reward definition of the caller's own clinic. Returns null both when the
// reward does not exist and when it belongs to another clinic - the caller must
// not be able to tell the two apart.
public sealed class GetRewardByIdHandler
{
    private readonly IRewardRepository _rewardRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetRewardByIdHandler(
        IRewardRepository rewardRepository,
        ICurrentTenant currentTenant)
    {
        _rewardRepository = rewardRepository;
        _currentTenant = currentTenant;
    }

    public async Task<RewardDefinitionDto?> HandleAsync(
        GetRewardByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        var reward = await _rewardRepository.GetByIdForTenantAsync(
            query.RewardId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        return reward is null ? null : RewardDefinitionDto.From(reward);
    }
}
