namespace DentalLoyalty.Application.Catalog.Queries;

// IsActive filters by status when set; null lists active and inactive services.
public sealed record GetServicesQuery(bool? IsActive = null);
