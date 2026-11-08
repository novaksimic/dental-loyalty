using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Loyalty.Queries;

public sealed record PatientRewardResult(
    Guid RewardId,
    string Name,
    string? Description,
    int RequiredPoints,
    int RequiredReferrals,
    RewardStatus Status);

public sealed record PatientRewardsResult(
    int Points,
    int ReferralBalance,
    IReadOnlyList<PatientRewardResult> Rewards);

// The patient's balances and the state of each of the clinic's rewards for them
// (BUSINESS.md §9, §12, §18): Locked, Available or Redeemed in the active cycle,
// each evaluated independently by Reward.StatusFor. Lists the rewards the clinic
// offers, plus any it no longer offers that were redeemed in the active cycle.
//
// Shown as of now (ADR-008): when the patient's cycle has expired, the view is
// of the next cycle - reset balances, rewards evaluated again - exactly as the
// next operation will record it. A read never saves that rollover.
//
// Tenant-scoped like the other patient use cases: returns null when the caller
// has no tenant, or the patient is not in the caller's clinic.
public sealed class GetPatientRewardsHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly IRewardRepository _rewardRepository;
    private readonly ILoyaltyAccountRepository _loyaltyAccountRepository;
    private readonly LoyaltyCycleProgression _loyaltyCycleProgression;
    private readonly ICurrentTenant _currentTenant;

    public GetPatientRewardsHandler(
        IPatientRepository patientRepository,
        IRewardRepository rewardRepository,
        ILoyaltyAccountRepository loyaltyAccountRepository,
        LoyaltyCycleProgression loyaltyCycleProgression,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _rewardRepository = rewardRepository;
        _loyaltyAccountRepository = loyaltyAccountRepository;
        _loyaltyCycleProgression = loyaltyCycleProgression;
        _currentTenant = currentTenant;
    }

    public async Task<PatientRewardsResult?> HandleAsync(
        GetPatientRewardsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        var clinicId = _currentTenant.ClinicId!.Value;

        var patient = await _patientRepository.GetByIdForTenantAsync(
            query.PatientId,
            clinicId,
            cancellationToken);

        if (patient is null)
        {
            return null;
        }

        // ADR-006: every Patient has its account from the moment it is created.
        var account = await _loyaltyAccountRepository.GetByPatientIdForTenantAsync(
            patient.Id,
            clinicId,
            cancellationToken)
            ?? throw new InvalidOperationException("The patient has no loyalty account.");

        var now = DateTime.UtcNow;

        var activeCycle = await _loyaltyCycleProgression.GetCurrentCycleAsync(
            account,
            now,
            save: false,
            cancellationToken);

        IReadOnlySet<Guid> redeemedRewardIds = activeCycle is null
            ? new HashSet<Guid>()
            : await _rewardRepository.GetRedeemedRewardIdsAsync(activeCycle.Id, cancellationToken);

        var rewards = await _rewardRepository.ListForTenantAsync(clinicId, cancellationToken);

        var results = PatientRewardStatuses.Evaluate(rewards, account, activeCycle, redeemedRewardIds, now);

        return new PatientRewardsResult(account.Points, account.ReferralBalance, results);
    }
}
