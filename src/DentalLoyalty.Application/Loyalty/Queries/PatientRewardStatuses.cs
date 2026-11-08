using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Loyalty.Queries;

// The state of each of the clinic's rewards for one patient (BUSINESS.md §9,
// §12, §17): Locked, Available or Redeemed in the patient's current cycle, each
// evaluated independently by Reward.StatusFor. Lists the rewards the clinic
// offers, plus any it no longer offers that were redeemed in that cycle. Shared
// by every view that presents the patient's rewards, so they always agree.
internal static class PatientRewardStatuses
{
    public static IReadOnlyList<PatientRewardResult> Evaluate(
        IEnumerable<Reward> clinicRewards,
        LoyaltyAccount account,
        LoyaltyCycle? currentCycle,
        IReadOnlySet<Guid> redeemedInCurrentCycle,
        DateTime now)
    {
        return clinicRewards
            .Where(r => r.IsActive || redeemedInCurrentCycle.Contains(r.Id))
            .OrderBy(r => r.RequiredPoints)
            .ThenBy(r => r.RequiredReferrals)
            .ThenBy(r => r.Name)
            .Select(r => new PatientRewardResult(
                r.Id,
                r.Name,
                r.Description,
                r.RequiredPoints,
                r.RequiredReferrals,
                r.StatusFor(account, currentCycle, redeemedInCurrentCycle.Contains(r.Id), now)))
            .ToList();
    }
}
