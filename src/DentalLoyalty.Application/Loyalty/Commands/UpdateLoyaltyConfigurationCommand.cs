namespace DentalLoyalty.Application.Loyalty.Commands;

// No ClinicId: only the caller's own clinic's configuration can be changed,
// resolved from the trusted tenant context (SECURITY.md §6.3).
public sealed record UpdateLoyaltyConfigurationCommand(
    decimal SpendAmountRsd,
    int PointsAwarded);
