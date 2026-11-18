using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Catalog.Commands;

public enum ServiceCommandStatus
{
    // Created or updated; Service is the saved service.
    Saved = 1,

    // The caller has no trusted clinic (tenant): nothing can be created.
    NoTenant = 2,

    // No such service in the caller's clinic - including a service of another
    // clinic, which is indistinguishable from none.
    NotFound = 3,

    // Rejected by the Service's validation before anything was saved.
    InvalidName = 4,
    InvalidPrice = 5,
}

public sealed record ServiceCommandResult(
    ServiceCommandStatus Status,
    Service? Service = null)
{
    public static ServiceCommandResult Rejected(ServiceCommandStatus status) => new(status);

    // Maps the Service's own validation (the single place its rules live) to a
    // result, so the API can answer 400 with the offending field.
    // An empty clinic id (a malformed tenant claim) is no tenant at all.
    internal static ServiceCommandResult Invalid(ArgumentException exception) =>
        Rejected(exception.ParamName switch
        {
            "price" => ServiceCommandStatus.InvalidPrice,
            "name" => ServiceCommandStatus.InvalidName,
            _ => ServiceCommandStatus.NoTenant,
        });
}
