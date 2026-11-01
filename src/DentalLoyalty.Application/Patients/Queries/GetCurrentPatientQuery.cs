namespace DentalLoyalty.Application.Patients.Queries;

// Deliberately carries no PatientId: the patient is identified solely by the
// authenticated caller's identity (ICurrentUser + ICurrentTenant), never by a
// client-supplied id.
public sealed record GetCurrentPatientQuery;
