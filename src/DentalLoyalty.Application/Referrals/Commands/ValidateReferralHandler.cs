using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Application.Loyalty;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Referrals.Commands;

// Records that the referred patient of a referral in the caller's own clinic
// completed the dental examination, which makes the referral valid (BUSINESS.md
// §4.1), and applies its referral rewards (§4.2; ADR-007) - all in one database
// transaction.
//
// Rewards are generated only if both patients were registered and linked when
// the examination took place; otherwise they are forfeited for both, and the
// referral is still recorded as valid for history. When generated, each
// patient receives 50,000 points (within the maximum balance, §3.3) and one
// referral credit, unless that patient already received the maximum number of
// referral rewards in the cycle, or the examination took place in a cycle of
// that patient that has since ended (Referral.Validate; ADR-008).
//
// The cycle lifecycle is applied through LoyaltyCycleProgression (ADR-008):
// each patient's expired cycle is ended and the next one started first, and a
// referral reward that satisfies a patient's last completion condition
// completes that cycle in the same database transaction as the referral.
//
// Every lookup is scoped to the authenticated tenant (ICurrentTenant). The
// Clinic Admin confirming the examination is the authenticated caller
// (ICurrentUser).
//
// Throws ConcurrencyConflictException when the referral, an account or a cycle
// changed concurrently and ReferralAlreadyValidatedException when the
// referral's rewards were already recorded; nothing is saved in either case.
public sealed class ValidateReferralHandler
{
    private readonly IReferralRepository _referralRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ILoyaltyAccountRepository _loyaltyAccountRepository;
    private readonly ILoyaltyCycleRepository _loyaltyCycleRepository;
    private readonly LoyaltyCycleProgression _loyaltyCycleProgression;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public ValidateReferralHandler(
        IReferralRepository referralRepository,
        IPatientRepository patientRepository,
        ILoyaltyAccountRepository loyaltyAccountRepository,
        ILoyaltyCycleRepository loyaltyCycleRepository,
        LoyaltyCycleProgression loyaltyCycleProgression,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _referralRepository = referralRepository;
        _patientRepository = patientRepository;
        _loyaltyAccountRepository = loyaltyAccountRepository;
        _loyaltyCycleRepository = loyaltyCycleRepository;
        _loyaltyCycleProgression = loyaltyCycleProgression;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<ValidateReferralResult> HandleAsync(
        ValidateReferralCommand command,
        CancellationToken cancellationToken = default)
    {
        // Fails closed like the other tenant-scoped use cases.
        if (!_currentTenant.HasTenant || !_currentUser.HasUser)
        {
            return ValidateReferralResult.Rejected(ValidateReferralStatus.ReferralNotFound);
        }

        var clinicId = _currentTenant.ClinicId!.Value;
        var validatedBy = _currentUser.UserId!.Value;
        var now = DateTime.UtcNow;
        var examinedAt = command.ExaminedAt ?? now;

        if (examinedAt.Kind != DateTimeKind.Utc || examinedAt > now)
        {
            return ValidateReferralResult.Rejected(ValidateReferralStatus.InvalidRequest);
        }

        var referral = await _referralRepository.GetByIdForTenantAsync(
            command.ReferralId,
            clinicId,
            cancellationToken);

        if (referral is null)
        {
            return ValidateReferralResult.Rejected(ValidateReferralStatus.ReferralNotFound);
        }

        if (referral.Status != ReferralStatus.Pending)
        {
            return ValidateReferralResult.Rejected(ValidateReferralStatus.NotPending);
        }

        var referrer = await GetPatientAsync(referral.ReferrerPatientId, clinicId, cancellationToken);
        var referred = await GetPatientAsync(referral.ReferredPatientId, clinicId, cancellationToken);

        if (!referrer.IsActive || !referred.IsActive)
        {
            return ValidateReferralResult.Rejected(ValidateReferralStatus.PatientInactive);
        }

        var referrerParticipant = await GetParticipantAsync(referrer, clinicId, now, cancellationToken);
        var referredParticipant = await GetParticipantAsync(referred, clinicId, now, cancellationToken);

        var validation = referral.Validate(referrerParticipant, referredParticipant, examinedAt, validatedBy);

        var referrerCompleted = await CompleteIfRewardedAsync(referrerParticipant, validation.Referrer, now, cancellationToken);
        var referredCompleted = await CompleteIfRewardedAsync(referredParticipant, validation.Referred, now, cancellationToken);

        await _loyaltyAccountRepository.SaveReferralValidationAsync(
            referral,
            referrerParticipant.Account,
            referredParticipant.Account,
            validation.Transactions,
            cancellationToken);

        return new ValidateReferralResult(
            ValidateReferralStatus.Validated,
            ResultFor(validation.Referrer, referrerParticipant, validation, referrerCompleted),
            ResultFor(validation.Referred, referredParticipant, validation, referredCompleted));
    }

    private static ReferralRewardResult ResultFor(
        ReferralRewardOutcome outcome,
        ReferralParticipant participant,
        ReferralValidation validation,
        bool cycleCompleted)
    {
        var credited = validation.Transactions
            .SingleOrDefault(t => t.PatientId == participant.Account.PatientId)?
            .PointsChange ?? 0;

        return new ReferralRewardResult(outcome, credited, cycleCompleted);
    }

    // Only a credited referral reward changes the account, so only it can
    // satisfy the cycle's last completion condition (BUSINESS.md §5.2).
    private async Task<bool> CompleteIfRewardedAsync(
        ReferralParticipant participant,
        ReferralRewardOutcome outcome,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (outcome != ReferralRewardOutcome.Rewarded)
        {
            return false;
        }

        var completion = await _loyaltyCycleProgression.CompleteIfSatisfiedAsync(
            participant.Account,
            participant.CurrentCycle!,
            now,
            cancellationToken: cancellationToken);

        return completion is not null;
    }

    // The patient's account, their cycle as of now (an expired one is ended and
    // the next started first) and when they registered and linked their account.
    // No benefits before registration and linking (BUSINESS.md §5.1): a patient
    // without a linked user account has no cycle to receive them.
    private async Task<ReferralParticipant> GetParticipantAsync(
        Patient patient,
        Guid clinicId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // ADR-006: every Patient has its account from the moment it is created.
        var account = await _loyaltyAccountRepository.GetByPatientIdForTenantAsync(patient.Id, clinicId, cancellationToken)
            ?? throw new InvalidOperationException("The patient has no loyalty account.");

        if (patient.UserId is null)
        {
            return new ReferralParticipant(account, null, null);
        }

        var currentCycle = await _loyaltyCycleProgression.GetCurrentCycleAsync(account, now, save: true, cancellationToken);
        var registeredAt = await _loyaltyCycleRepository.GetRegisteredAtAsync(patient.Id, clinicId, cancellationToken);

        return new ReferralParticipant(account, currentCycle, registeredAt);
    }

    // The referral's patients are in its clinic (DATA.md §10).
    private async Task<Patient> GetPatientAsync(Guid patientId, Guid clinicId, CancellationToken cancellationToken) =>
        await _patientRepository.GetByIdForTenantAsync(patientId, clinicId, cancellationToken)
            ?? throw new InvalidOperationException("The referral's patient is not in its clinic.");
}
