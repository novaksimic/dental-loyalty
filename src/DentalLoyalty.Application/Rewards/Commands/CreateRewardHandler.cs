using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Rewards.Commands;

// Adds a reward definition to the caller's own clinic (BUSINESS.md §10, §18
// "manage reward definitions", §22). Rewards are created only this way - no
// clinic is seeded with defaults. The clinic is the authenticated tenant
// (ICurrentTenant), never a value from the request. A new reward is active.
public sealed class CreateRewardHandler
{
    private readonly IRewardRepository _rewardRepository;
    private readonly ICurrentTenant _currentTenant;

    public CreateRewardHandler(
        IRewardRepository rewardRepository,
        ICurrentTenant currentTenant)
    {
        _rewardRepository = rewardRepository;
        _currentTenant = currentTenant;
    }

    public async Task<RewardCommandResult> HandleAsync(
        CreateRewardCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return RewardCommandResult.Rejected(RewardCommandStatus.NoTenant);
        }

        Reward reward;

        try
        {
            reward = new Reward(
                _currentTenant.ClinicId!.Value,
                RewardInput.Name(command.Name),
                command.RequiredPoints,
                command.RequiredReferrals,
                RewardInput.Description(command.Description));
        }
        catch (ArgumentException ex)
        {
            return RewardCommandResult.Invalid(ex);
        }

        await _rewardRepository.AddAsync(reward, cancellationToken);

        return new RewardCommandResult(RewardCommandStatus.Saved, RewardDefinitionDto.From(reward));
    }
}
