namespace DentalLoyalty.Application.Identity.Commands;

public sealed record CreateClinicAdminCommand(
    Guid ClinicId,
    string Email,
    string Password);
