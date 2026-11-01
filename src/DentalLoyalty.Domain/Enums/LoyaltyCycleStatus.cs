namespace DentalLoyalty.Domain.Enums;

// BUSINESS.md §5.1-§5.4: a cycle is active until it ends in exactly one of two
// ways. Expiry is never a successful completion (§5.3).
public enum LoyaltyCycleStatus
{
    Active = 1,

    // All completion conditions were met (§5.2).
    Completed = 2,

    // One year passed without completion (§5.3).
    Expired = 3
}
