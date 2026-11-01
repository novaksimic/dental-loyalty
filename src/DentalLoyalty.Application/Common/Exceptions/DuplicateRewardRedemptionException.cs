namespace DentalLoyalty.Application.Common.Exceptions;

// The reward has already been redeemed in the patient's active cycle (by a
// repeated or concurrent request). Nothing from the failed operation was
// persisted, so the reward is not redeemed twice. Raised by Infrastructure in
// place of the EF Core exception; the API maps it to 409 Conflict.
public sealed class DuplicateRewardRedemptionException : Exception
{
    public DuplicateRewardRedemptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
