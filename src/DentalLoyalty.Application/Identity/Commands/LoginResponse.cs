namespace DentalLoyalty.Application.Identity.Commands;

public sealed record LoginResponse(
    string Token,
    Guid UserId,
    string Role,
    Guid? ClinicId);
