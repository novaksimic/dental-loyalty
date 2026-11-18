namespace DentalLoyalty.Application.Identity.Commands;

public enum CreateClinicAdminStatus
{
    Created = 1,

    // The email is not a well-formed address (EmailAddress).
    InvalidEmail = 2,

    // The password does not satisfy PasswordPolicy.
    InvalidPassword = 3,

    // No clinic with the requested id.
    ClinicNotFound = 4,

    // A user - of any role, in any clinic - already has this email.
    EmailAlreadyExists = 5,
}

// Email is the normalized form the account was stored with. Never carries the
// password or its hash.
public sealed record CreateClinicAdminResult(
    CreateClinicAdminStatus Status,
    Guid? UserId = null,
    string? Email = null)
{
    public static CreateClinicAdminResult Rejected(CreateClinicAdminStatus status) => new(status);
}
