namespace DentalLoyalty.Application.Rewards.Commands;

// Deliberately carries no clinic: the reward is created in the caller's own
// clinic (ICurrentTenant).
public sealed record CreateRewardCommand(
    string Name,
    string? Description,
    int RequiredPoints,
    int RequiredReferrals);
