namespace DentalLoyalty.Application.Common.Exceptions;

// The referral's rewards were already recorded (by a repeated or concurrent
// validation). Nothing from the failed operation was persisted, so no patient
// is rewarded twice for one referral. Raised by Infrastructure in place of the
// EF Core exception; the API maps it to 409 Conflict.
public sealed class ReferralAlreadyValidatedException : Exception
{
    public ReferralAlreadyValidatedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
