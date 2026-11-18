using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

public interface IPatientRepository
{
    // ADR-006: a Patient is always persisted together with its LoyaltyAccount, in
    // one save, so a Patient can never exist without one (and vice versa). The
    // account must belong to this Patient and its clinic.
    Task AddAsync(
        Patient patient,
        LoyaltyAccount loyaltyAccount,
        CancellationToken cancellationToken = default);

    // Persists a new User, the Patient's link to it and the patient's first loyalty
    // cycle (BUSINESS.md §5.1) in one save: either all of them are saved or none
    // is. The Patient must have been loaded through this repository with no linked
    // User - an existing link is never replaced - and now be linked to user; the
    // cycle must belong to it.
    //
    // Throws DuplicateEmailException when another user already has the user's
    // email, and ConcurrencyConflictException when another operation linked the
    // Patient after it was loaded; nothing is saved in either case.
    Task LinkUserAsync(
        Patient patient,
        User user,
        LoyaltyCycle firstCycle,
        CancellationToken cancellationToken = default);

    // Not tenant-scoped - only safe for callers that are not acting on behalf of
    // a specific tenant-scoped request. No production use case calls it today.
    // Tenant-scoped/HTTP-reachable
    // use cases must use GetByIdForTenantAsync or GetByIdForPatientOwnerAsync
    // instead so the lookup itself enforces the tenant/ownership boundary.
    Task<Patient?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    // Returns the patient only if it exists AND belongs to clinicId - a patient
    // belonging to another clinic is indistinguishable from a nonexistent one
    // (both return null), so callers can safely map null to 404 without leaking
    // whether the resource exists in another tenant.
    Task<Patient?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // Same as GetByIdForTenantAsync, plus the additional constraint that the
    // patient's linked User account is userId - the reusable pattern for
    // patient self-service ("own record only") use cases in a later slice.
    Task<Patient?> GetByIdForPatientOwnerAsync(
        Guid id,
        Guid clinicId,
        Guid userId,
        CancellationToken cancellationToken = default);

    // Patient self-service lookup ("/me"): returns the patient linked to userId
    // only if it also belongs to clinicId. Both values must come from the trusted
    // authentication context, never from the request. A User whose linked
    // Patient sits in another clinic resolves to null, same as no Patient at all.
    Task<Patient?> GetByUserIdForTenantAsync(
        Guid userId,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        Patient patient,
        CancellationToken cancellationToken = default);

    // One page of clinicId's patients, ordered by last name, first name and id,
    // with the total number that match. Filtered in the database by clinic, by
    // status when isActive is set, and by every search term: each must occur
    // (case-insensitively where the database collation is) in the first name,
    // last name, email or phone number. Wildcard characters in a term match
    // themselves. Read-only: the patients returned are not tracked.
    Task<PatientPage> ListForTenantAsync(
        Guid clinicId,
        IReadOnlyList<string> searchTerms,
        bool? isActive,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}

public sealed record PatientPage(
    IReadOnlyList<Patient> Items,
    int TotalCount);