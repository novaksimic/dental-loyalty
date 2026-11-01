namespace DentalLoyalty.Application.Common.Exceptions;

// The referred patient already has a referral (from a repeated or concurrent
// request): a patient is referred at most once. Nothing from the failed
// operation was persisted. Raised by Infrastructure in place of the EF Core
// exception; the API maps it to 409 Conflict.
public sealed class DuplicateReferralException : Exception
{
    public DuplicateReferralException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
