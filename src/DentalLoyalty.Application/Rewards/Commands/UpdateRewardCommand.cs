namespace DentalLoyalty.Application.Rewards.Commands;

// Replaces the reward's name, description and requirements.
public sealed record UpdateRewardCommand(
    Guid RewardId,
    string Name,
    string? Description,
    int RequiredPoints,
    int RequiredReferrals);
