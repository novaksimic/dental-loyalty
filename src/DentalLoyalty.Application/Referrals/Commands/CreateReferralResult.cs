namespace DentalLoyalty.Application.Referrals.Commands;

public enum CreateReferralStatus
{
    // Recorded as Pending; it generates nothing until it is validated.
    Created = 1,

    // A patient cannot refer themselves (DATA.md §10).
    SelfReferral = 2,

    // No such referring or referred patient in the caller's clinic.
    PatientNotFound = 3,

    PatientInactive = 4,

    // The referred patient already has a referral: a patient is referred at most
    // once.
    AlreadyReferred = 5,
}

public sealed record CreateReferralResult(
    CreateReferralStatus Status,
    Guid? ReferralId = null)
{
    public static CreateReferralResult Rejected(CreateReferralStatus status) => new(status);
}
