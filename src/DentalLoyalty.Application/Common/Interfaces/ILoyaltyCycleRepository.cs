using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

public interface ILoyaltyCycleRepository
{
    // The patient's active cycle (at most one, DATA.md §14), only if it belongs to
    // clinicId; null when the patient has none - for example before registration
    // and account linking (BUSINESS.md §5.1). clinicId must come from the trusted
    // tenant context (ICurrentTenant), never from the request.
    Task<LoyaltyCycle?> GetActiveForPatientAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // When the patient registered and linked their user account - the start of
    // their first cycle (BUSINESS.md §5.1) - or null when they have not.
    Task<DateTime?> GetRegisteredAtAsync(
        Guid patientId,
        Guid clinicId,
        CancellationToken cancellationToken = default);
}
