namespace DentalLoyalty.Application.Referrals.Commands;

// A Clinic Admin confirms that the referred patient completed their dental
// examination, which makes the referral valid (BUSINESS.md §4.1; ADR-007).
// ExaminedAt is a UTC time; null means now. There is deliberately no clinic,
// points or referral value: they come from the tenant and the business rules.
public sealed record ValidateReferralCommand(
    Guid ReferralId,
    DateTime? ExaminedAt);
