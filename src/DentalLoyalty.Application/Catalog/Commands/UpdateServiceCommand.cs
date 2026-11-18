namespace DentalLoyalty.Application.Catalog.Commands;

// Replaces the service's name and price. Price is in RSD.
public sealed record UpdateServiceCommand(
    Guid ServiceId,
    string Name,
    decimal Price);
