namespace DentalLoyalty.Application.Common.Exceptions;

// A loyalty account was changed by another operation between being loaded and
// being saved (ADR-005). Nothing from the failed operation was persisted. Raised
// by Infrastructure in place of the EF Core exception so Application and API do
// not depend on EF Core; the API maps it to 409 Conflict.
//
// This only reports a concurrent update. It does not mean the operation can be
// resubmitted blindly - a resubmitted operation is evaluated against the current
// state, and duplicate business operations are prevented by their own checks.
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
