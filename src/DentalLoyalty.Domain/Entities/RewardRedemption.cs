namespace DentalLoyalty.Domain.Entities;

// The historical fact that a patient used (redeemed) a reward they had unlocked,
// confirmed by a Clinic Admin (BUSINESS.md §13.1, §19; DATA.md §13). Created only
// by LoyaltyAccount.RedeemReward, together with its RewardRedeemed transaction.
//
// It records no points or referrals spent: redemption consumes neither
// (BUSINESS.md §13). It belongs to the cycle it was redeemed in, so a reward is
// redeemed at most once per cycle (enforced by the database) and its availability
// is evaluated again in a new cycle (§5.4).
public class RewardRedemption
{
    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid RewardId { get; private set; }

    public Guid LoyaltyCycleId { get; private set; }

    public DateTime RedeemedAt { get; private set; }

    // The Clinic Admin who confirmed the redemption (BUSINESS.md §13.1).
    public Guid RedeemedBy { get; private set; }

    private RewardRedemption()
    {
    }

    internal RewardRedemption(
        LoyaltyAccount account,
        Reward reward,
        LoyaltyCycle cycle,
        Guid redeemedBy,
        DateTime redeemedAt)
    {
        Id = Guid.NewGuid();
        ClinicId = account.ClinicId;
        PatientId = account.PatientId;
        RewardId = reward.Id;
        LoyaltyCycleId = cycle.Id;
        RedeemedAt = redeemedAt;
        RedeemedBy = redeemedBy;
    }
}
