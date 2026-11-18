namespace DentalLoyalty.Application.Common.Exceptions;

// Another user already has this email (a concurrent request won the race past
// the duplicate check): Users.Email is unique platform-wide (ADR-010). Nothing
// from the failed operation was persisted. Raised by Infrastructure in place of
// the EF Core exception; the API maps it to 409 Conflict.
public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
