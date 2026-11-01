namespace DentalLoyalty.Domain.Enums;

// What one patient received when a referral was validated (BUSINESS.md §4.2;
// ADR-007, ADR-008).
public enum ReferralRewardOutcome
{
    // 50,000 points (fewer only at the maximum balance) and one referral credit.
    Rewarded = 1,

    // Either patient was not registered and linked when the referral became
    // valid: nothing for either patient, permanently (§4.2).
    Forfeited = 2,

    // This patient had already received the maximum number of referral rewards
    // in the cycle; the other patient is unaffected (ADR-007).
    MaximumReached = 3,

    // The referral became valid in a cycle of this patient that has since ended:
    // late activity is not credited to the next cycle (ADR-008). The other
    // patient is unaffected.
    CycleEnded = 4,
}
