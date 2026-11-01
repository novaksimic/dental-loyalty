namespace DentalLoyalty.Application.Loyalty.Queries;

// No ClinicId: the configuration is always the caller's own clinic's, resolved
// from the trusted tenant context (SECURITY.md §6.3).
public sealed record GetLoyaltyConfigurationQuery;
