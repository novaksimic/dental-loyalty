using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Common.Interfaces;

// Referrals between patients of one clinic (BUSINESS.md §4; DATA.md §10).
// clinicId must come from the trusted tenant context (ICurrentTenant), never from
// the request. Validating a referral changes loyalty balances, so it is saved
// through ILoyaltyAccountRepository.SaveReferralValidationAsync.
public interface IReferralRepository
{
    // Returns the referral only if it belongs to clinicId - a referral of another
    // clinic is indistinguishable from none. Tracked, so the RowVersion read here
    // is the one checked when it is saved.
    Task<Referral?> GetByIdForTenantAsync(
        Guid id,
        Guid clinicId,
        CancellationToken cancellationToken = default);

    // Whether the patient already has a referral that is not cancelled.
    Task<bool> ExistsForReferredPatientAsync(
        Guid referredPatientId,
        CancellationToken cancellationToken = default);

    // Saves a newly recorded referral.
    //
    // Throws DuplicateReferralException if the referred patient already has a
    // referral; nothing is persisted in that case.
    Task AddAsync(
        Referral referral,
        CancellationToken cancellationToken = default);
}
