namespace DentalLoyalty.Application.Patients.Commands;

public enum RecordPatientServiceStatus
{
    // Recorded; the patient's active cycle accepted it and a ServicePointsEarned
    // transaction was written (credited points may be 0 at the maximum balance).
    RecordedWithPoints = 1,

    // Recorded for clinic history only: the patient was not eligible for points
    // for this service (BUSINESS.md §3.1, §5.1). No loyalty transaction.
    RecordedWithoutPoints = 2,

    // The request was invalid (empty occurrence id, performance time in the future).
    InvalidRequest = 3,

    // No such patient - or catalog service - in the caller's clinic.
    PatientNotFound = 4,
    ServiceNotFound = 5,

    PatientInactive = 6,
    ServiceInactive = 7,

    // This service occurrence has already been recorded; nothing changed, and it
    // is not recorded or credited again.
    AlreadyRecorded = 9,
}

public sealed record RecordPatientServiceResult(
    RecordPatientServiceStatus Status,
    Guid? PatientServiceId = null,
    int EarnedPoints = 0,
    int CreditedPoints = 0,
    // The points completed the patient's cycle and started the next one (§5.2).
    bool CycleCompleted = false)
{
    public static RecordPatientServiceResult Rejected(RecordPatientServiceStatus status) => new(status);
}
