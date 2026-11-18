namespace DentalLoyalty.Application.Catalog.Commands;

// Deliberately carries no clinic: the service is created in the caller's own
// clinic (ICurrentTenant). Price is in RSD.
public sealed record CreateServiceCommand(
    string Name,
    decimal Price);
