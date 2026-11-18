namespace DentalLoyalty.Application.Catalog.Commands;

public sealed record SetServiceActiveCommand(
    Guid ServiceId,
    bool IsActive);
