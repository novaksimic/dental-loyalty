namespace DentalLoyalty.Application.Patients.Commands;

// PatientServiceId identifies the service occurrence and is chosen by the caller,
// so a repeated submission of the same occurrence is recognised and never
// recorded or credited twice. PerformedAt is a UTC time; null means now. There is
// deliberately no clinic, price or points value: the clinic is the caller's own
// tenant, and the price and points come from the clinic's catalog and scoring
// configuration (BUSINESS.md §3.1, §20).
public sealed record RecordPatientServiceCommand(
    Guid PatientId,
    Guid PatientServiceId,
    Guid ServiceId,
    DateTime? PerformedAt);
