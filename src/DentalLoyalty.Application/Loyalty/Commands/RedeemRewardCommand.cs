namespace DentalLoyalty.Application.Loyalty.Commands;

// A Clinic Admin confirms that a patient of their own clinic used a reward
// (BUSINESS.md §13.1, §19). There is deliberately no clinic, balance or cycle
// value: they come from the tenant and the patient's current loyalty state.
public sealed record RedeemRewardCommand(
    Guid PatientId,
    Guid RewardId);
