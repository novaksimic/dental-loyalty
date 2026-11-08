namespace DentalLoyalty.Application.Loyalty.Queries;

// Whose loyalty history is read. CurrentPatient carries no id at all: the
// patient is resolved from the authenticated user within the authenticated
// tenant (SECURITY.md §5.3, §7.2), so a patient cannot reach another patient's
// history by changing a value in the request. Patient(id) is the Clinic Admin's
// view of a patient of their own clinic (BUSINESS.md §18; SECURITY.md §7.3).
public sealed record LoyaltyHistorySubject
{
    private LoyaltyHistorySubject(Guid? patientId)
    {
        PatientId = patientId;
    }

    // Null for the authenticated patient.
    public Guid? PatientId { get; }

    public static LoyaltyHistorySubject CurrentPatient { get; } = new((Guid?)null);

    public static LoyaltyHistorySubject Patient(Guid patientId) => new(patientId);
}
