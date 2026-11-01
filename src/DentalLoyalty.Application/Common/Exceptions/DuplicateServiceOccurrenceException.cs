namespace DentalLoyalty.Application.Common.Exceptions;

// A service occurrence with this id has already been recorded (by a repeated or
// concurrent submission). Nothing from the failed operation was persisted, so
// the occurrence is not recorded - or credited - twice. Raised by Infrastructure
// in place of the EF Core exception; the API maps it to 409 Conflict.
public sealed class DuplicateServiceOccurrenceException : Exception
{
    public DuplicateServiceOccurrenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
