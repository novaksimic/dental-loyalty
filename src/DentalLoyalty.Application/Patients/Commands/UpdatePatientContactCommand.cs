namespace DentalLoyalty.Application.Patients.Commands;

public sealed record UpdatePatientContactCommand(
    Guid PatientId,
    string PhoneNumber,
    string? Email);
