namespace DentalLoyalty.Application.Identity.Commands;

public enum CreatePatientUserStatus
{
    Created = 1,

    // The email is not a well-formed address (EmailAddress).
    InvalidEmail = 2,

    // The password does not satisfy PasswordPolicy.
    InvalidPassword = 3,

    // No such patient in the caller's clinic - including a patient of another
    // clinic, which is indistinguishable from none.
    PatientNotFound = 4,

    // The patient record is deactivated; like every other loyalty operation, no
    // account is created for it.
    PatientInactive = 5,

    // The patient already has a user account; a link is never replaced.
    AlreadyLinked = 6,

    // A user - of any role, in any clinic - already has this email.
    EmailAlreadyExists = 7,
}

// Email is the normalized form the account was stored with. Never carries the
// password or its hash.
public sealed record CreatePatientUserResult(
    CreatePatientUserStatus Status,
    Guid? UserId = null,
    string? Email = null)
{
    public static CreatePatientUserResult Rejected(CreatePatientUserStatus status) => new(status);
}
