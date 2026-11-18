namespace DentalLoyalty.Application.Rewards.Commands;

public sealed record SetRewardActiveCommand(
    Guid RewardId,
    bool IsActive);
