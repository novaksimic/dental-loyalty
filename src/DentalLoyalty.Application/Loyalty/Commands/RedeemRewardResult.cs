namespace DentalLoyalty.Application.Loyalty.Commands;

public enum RedeemRewardStatus
{
    // The redemption and its RewardRedeemed transaction were recorded; no points
    // or referral credits were deducted (BUSINESS.md §13).
    Redeemed = 1,

    // No such patient - or reward - in the caller's clinic.
    PatientNotFound = 2,
    RewardNotFound = 3,

    PatientInactive = 4,

    // The clinic no longer offers the reward (BUSINESS.md §20, §22).
    RewardUnavailable = 5,

    // The patient has not unlocked the reward: a points or referral requirement
    // is not met, or the patient has no active cycle because they have not
    // registered and linked their account yet (BUSINESS.md §5.1, §10, §20).
    NotEligible = 6,

    // The reward was already redeemed in the patient's active cycle.
    AlreadyRedeemed = 7,
}

public sealed record RedeemRewardResult(
    RedeemRewardStatus Status,
    Guid? RedemptionId = null,
    DateTime? RedeemedAt = null,
    // The redemption completed the patient's cycle and started the next one
    // (BUSINESS.md §5.2, §19 step 5).
    bool CycleCompleted = false)
{
    public static RedeemRewardResult Rejected(RedeemRewardStatus status) => new(status);
}
