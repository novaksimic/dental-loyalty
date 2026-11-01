using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

public interface IPatientServiceRepository
{
    // Whether a service occurrence with this id has been recorded in any clinic.
    // Deliberately not tenant-scoped: occurrence ids are unique across clinics, so
    // an id taken elsewhere is the same conflict - and only true/false is revealed.
    // The unique key in the database remains the guard against concurrent saves.
    Task<bool> ExistsAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    // Records a service occurrence that earns no points (BUSINESS.md §3.1, §5.1),
    // for clinic history only - no account change and no loyalty transaction.
    // A service that earns points is saved with its account change and
    // transaction through ILoyaltyAccountRepository.SaveServicePointsAsync.
    //
    // Throws DuplicateServiceOccurrenceException if an occurrence with the same
    // id already exists; nothing is persisted in that case.
    Task AddAsync(
        PatientService patientService,
        CancellationToken cancellationToken = default);
}
