namespace DentalLoyalty.Domain.Exceptions;

// A business-rule violation (BUSINESS.md §20), as opposed to a malformed argument
// (null, empty id, negative amount), which keeps using the ArgumentException family
// like the existing entities do. Callers can map this to a business-rule error
// without treating it as an unexpected failure.
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
