namespace DentalLoyalty.Application.Patients.Commands;

public sealed record CreatePatientCommand(
    Guid ClinicId,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? Email);