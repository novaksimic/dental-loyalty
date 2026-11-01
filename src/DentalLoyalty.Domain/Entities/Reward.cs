using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Domain.Entities;

public class Reward
{
    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public int RequiredPoints { get; private set; }

    public int RequiredReferrals { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private Reward()
    {
        Name = null!;
    }

    public Reward(
        Guid clinicId,
        string name,
        int requiredPoints,
        int requiredReferrals = 0,
        string? description = null)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("ClinicId is required.", nameof(clinicId));
        }

        Validate(name, requiredPoints, requiredReferrals);

        Id = Guid.NewGuid();
        ClinicId = clinicId;
        Name = name;
        RequiredPoints = requiredPoints;
        RequiredReferrals = requiredReferrals;
        Description = description;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(
        string name,
        int requiredPoints,
        int requiredReferrals,
        string? description)
    {
        Validate(name, requiredPoints, requiredReferrals);

        Name = name;
        RequiredPoints = requiredPoints;
        RequiredReferrals = requiredReferrals;
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    // BUSINESS.md §10: unlocked only when every requirement is met - points and
    // referrals both. Compared against the account's current balances, which a
    // redemption never reduces (§12, §13), so redeeming one reward never locks
    // another. Not the same as the maximum balance: that caps the points, this
    // only reads them.
    public bool IsUnlockedBy(LoyaltyAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.ClinicId == ClinicId
            && account.Points >= RequiredPoints
            && account.ReferralBalance >= RequiredReferrals;
    }

    // The reward's state for the patient of account at the given time
    // (BUSINESS.md §9, §13.1): Redeemed when it was already redeemed in the
    // patient's active cycle; Available when that cycle is still running, the
    // clinic offers the reward and the patient has unlocked it; otherwise Locked.
    // A patient without an active cycle - not yet registered and linked - has no
    // rewards at all (§5.1).
    public RewardStatus StatusFor(
        LoyaltyAccount account,
        LoyaltyCycle? activeCycle,
        bool redeemedInCycle,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (redeemedInCycle)
        {
            return RewardStatus.Redeemed;
        }

        return activeCycle is not null
            && activeCycle.Status == LoyaltyCycleStatus.Active
            && activeCycle.PatientId == account.PatientId
            && !activeCycle.HasExpiredAt(at)
            && IsActive
            && IsUnlockedBy(account)
                ? RewardStatus.Available
                : RewardStatus.Locked;
    }

    // Requirements are compared against the patient's balances (BUSINESS.md §10),
    // so they cannot be negative, and a points requirement above the maximum
    // balance (BUSINESS.md §5/§22) could never be met.
    private static void Validate(
        string name,
        int requiredPoints,
        int requiredReferrals)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Reward name is required.", nameof(name));
        }

        if (requiredPoints < 0 || requiredPoints > LoyaltyAccount.MaximumPointsBalance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredPoints),
                requiredPoints,
                $"Required points must be between 0 and {LoyaltyAccount.MaximumPointsBalance}.");
        }

        if (requiredReferrals < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredReferrals),
                requiredReferrals,
                "Required referrals cannot be negative.");
        }
    }
}