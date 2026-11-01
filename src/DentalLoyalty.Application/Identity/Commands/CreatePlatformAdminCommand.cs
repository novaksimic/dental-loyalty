namespace DentalLoyalty.Application.Identity.Commands;

public sealed record CreatePlatformAdminCommand(
    string Email,
    string Password);
