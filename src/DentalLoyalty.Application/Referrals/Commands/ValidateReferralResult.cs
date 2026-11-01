using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Referrals.Commands;

public enum ValidateReferralStatus
{
    // The referral is now valid (Completed); see the per-patient outcomes.
    Validated = 1,

    // The examination time was not a UTC time, or is in the future.
    InvalidRequest = 2,

    // No such referral in the caller's clinic.
    ReferralNotFound = 3,

    PatientInactive = 4,

    // The referral was already validated, or was cancelled.
    NotPending = 5,
}

// What one patient received (ReferralRewardOutcome), the points credited, and
// whether the reward completed their cycle and started the next one (§5.2).
public sealed record ReferralRewardResult(
    ReferralRewardOutcome Outcome,
    int CreditedPoints = 0,
    bool CycleCompleted = false);

public sealed record ValidateReferralResult(
    ValidateReferralStatus Status,
    ReferralRewardResult? Referrer = null,
    ReferralRewardResult? Referred = null)
{
    public static ValidateReferralResult Rejected(ValidateReferralStatus status) => new(status);
}
