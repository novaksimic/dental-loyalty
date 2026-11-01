namespace DentalLoyalty.Domain.Enums;

// Transaction types listed in BUSINESS.md §14 (and BACKEND & API ARCHITECTURE.md §23).
public enum LoyaltyTransactionType
{
    ServicePointsEarned = 1,
    ReferralPointsEarned = 2,
    RewardRedeemed = 3,
    ManualPointsAdjustment = 4,
    ManualReferralAdjustment = 5,

    // BUSINESS.md §5.4, §14: the end of a cycle reset the active points and
    // referral progress to zero.
    CycleReset = 6
}
