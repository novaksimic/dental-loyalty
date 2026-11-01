using DentalLoyalty.Domain.Exceptions;
using DentalLoyalty.Domain.Loyalty;

namespace DentalLoyalty.Domain.Entities;

// One occurrence of a catalog service performed for a patient. It is recorded for
// clinic history whether or not it earns loyalty points; the points actually
// credited are recorded by the LoyaltyTransaction that LoyaltyAccount
// .EarnServicePoints returns for it - at most one per occurrence.
public class PatientService
{
    public Guid Id { get; private set; }

    public Guid ClinicId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid ServiceId { get; private set; }

    // The catalog price the points were calculated from (BUSINESS.md §3.1, §15).
    public decimal PriceAtTimeOfService { get; private set; }

    // Points this service generates (BUSINESS.md §3.1), before the maximum
    // balance is applied - 0 when it earns no points because the patient was not
    // eligible for it (BUSINESS.md §3.1, §5.1).
    public int EarnedPoints { get; private set; }

    // When the service was performed.
    public DateTime UsedAt { get; private set; }

    // When the service was recorded, and by whom.
    public DateTime RecordedAt { get; private set; }

    // Null only for services recorded before the recording user was stored.
    public Guid? RecordedBy { get; private set; }

    private PatientService()
    {
    }

    // Points are always calculated here from the catalog price and the scoring
    // configuration of the service's clinic (BUSINESS.md §3.1) and never supplied
    // by the caller (BUSINESS.md §20). They are calculated only when the patient's
    // active cycle accepts the service (LoyaltyCycle.AcceptsService); otherwise
    // the service is recorded with 0 points.
    //
    // id identifies this occurrence, so the same occurrence can never be recorded
    // - or earn points - twice.
    public PatientService(
        Guid id,
        Patient patient,
        Service service,
        LoyaltyConfiguration scoringConfiguration,
        LoyaltyCycle? activeCycle,
        DateTime performedAt,
        Guid recordedBy)
    {
        ArgumentNullException.ThrowIfNull(patient);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(scoringConfiguration);

        if (id == Guid.Empty)
        {
            throw new ArgumentException("The service occurrence id is required.", nameof(id));
        }

        if (recordedBy == Guid.Empty)
        {
            throw new ArgumentException("The user recording the service is required.", nameof(recordedBy));
        }

        var recordedAt = DateTime.UtcNow;

        if (performedAt.Kind != DateTimeKind.Utc || performedAt > recordedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(performedAt),
                performedAt,
                "The performance time must be a UTC time that is not in the future.");
        }

        // BUSINESS.md §21: a clinic can only use its own services for its own patients.
        if (patient.ClinicId != service.ClinicId)
        {
            throw new DomainException(
                "The service does not belong to the patient's clinic.");
        }

        // BUSINESS.md §3.1: a scoring configuration is never applied to another
        // clinic's services.
        if (scoringConfiguration.ClinicId != service.ClinicId)
        {
            throw new DomainException(
                "The scoring configuration does not belong to the service's clinic.");
        }

        if (!patient.IsActive)
        {
            throw new DomainException(
                "A service cannot be recorded for an inactive patient.");
        }

        if (!service.IsActive)
        {
            throw new DomainException(
                "An inactive catalog service cannot be recorded.");
        }

        if (activeCycle is not null)
        {
            if (activeCycle.PatientId != patient.Id || activeCycle.ClinicId != patient.ClinicId)
            {
                throw new DomainException(
                    "The loyalty cycle does not belong to the patient.");
            }

            // Points must never go to an expired cycle, and the next cycle is not
            // started here, so nothing is recorded against an expired one.
            if (activeCycle.HasExpiredAt(recordedAt))
            {
                throw new DomainException(
                    "The patient's loyalty cycle has expired.");
            }
        }

        Id = id;
        ClinicId = service.ClinicId;
        PatientId = patient.Id;
        ServiceId = service.Id;
        PriceAtTimeOfService = service.Price;
        UsedAt = performedAt;
        RecordedAt = recordedAt;
        RecordedBy = recordedBy;

        // A cycle exists only once the patient is registered and linked
        // (LoyaltyCycle.StartFirst); the link is checked here as well.
        if (patient.UserId is not null && activeCycle?.AcceptsService(this) == true)
        {
            var points = LoyaltyPointsCalculator.CalculatePoints(service.Price, scoringConfiguration);

            if (points > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(service),
                    service.Price,
                    "The service price generates more points than can be recorded for one service.");
            }

            EarnedPoints = (int)points;
        }
    }
}
