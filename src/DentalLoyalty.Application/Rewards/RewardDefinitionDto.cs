using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Rewards;

// A clinic's reward definition as administration sees it (BUSINESS.md §10, §18,
// §22): what it is, what unlocks it, and whether the clinic offers it. Carries
// no clinic id (always the caller's own) and no per-patient state - that is the
// patient rewards view (Locked/Available/Redeemed).
public sealed record RewardDefinitionDto(
    Guid Id,
    string Name,
    string? Description,
    int RequiredPoints,
    int RequiredReferrals,
    bool IsActive)
{
    public static RewardDefinitionDto From(Reward reward) => new(
        reward.Id,
        reward.Name,
        reward.Description,
        reward.RequiredPoints,
        reward.RequiredReferrals,
        reward.IsActive);
}
