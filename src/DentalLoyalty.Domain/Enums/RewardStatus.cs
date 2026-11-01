namespace DentalLoyalty.Domain.Enums;

// A reward's state for one patient in their active cycle (BUSINESS.md §9, §13.1).
// Derived from the patient's balances and redemptions, never stored.
public enum RewardStatus
{
    Locked = 1,

    // Unlocked: every requirement is met and the reward can be redeemed.
    Available = 2,

    // Used in the patient's active cycle.
    Redeemed = 3
}
