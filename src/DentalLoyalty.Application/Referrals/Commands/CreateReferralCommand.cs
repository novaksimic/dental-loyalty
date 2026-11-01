namespace DentalLoyalty.Application.Referrals.Commands;

// A Clinic Admin records that a patient of their own clinic referred another
// patient of it (BUSINESS.md §4.1; ADR-007). There is deliberately no clinic
// value: it is the caller's own tenant.
public sealed record CreateReferralCommand(
    Guid ReferrerPatientId,
    Guid ReferredPatientId);
