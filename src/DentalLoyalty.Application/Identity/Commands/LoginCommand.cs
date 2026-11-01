namespace DentalLoyalty.Application.Identity.Commands;

public sealed record LoginCommand(
    string Email,
    string Password);
