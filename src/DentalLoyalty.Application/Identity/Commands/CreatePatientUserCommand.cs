namespace DentalLoyalty.Application.Identity.Commands;

public sealed record CreatePatientUserCommand(
    Guid PatientId,
    string Email,
    string Password);
