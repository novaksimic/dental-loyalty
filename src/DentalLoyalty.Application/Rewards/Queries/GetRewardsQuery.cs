namespace DentalLoyalty.Application.Rewards.Queries;

// IsActive filters by status when set; null lists active and inactive rewards.
public sealed record GetRewardsQuery(bool? IsActive = null);

public sealed record GetRewardByIdQuery(Guid RewardId);
