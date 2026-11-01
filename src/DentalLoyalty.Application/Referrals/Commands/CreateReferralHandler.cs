using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Referrals.Commands;

// Records a Pending referral between two patients of the caller's own clinic
// (BUSINESS.md §4.1; DATA.md §10; ADR-007). It changes no balances: a referral
// generates rewards only when it is validated (ValidateReferralHandler).
//
// Every lookup is scoped to the authenticated tenant (ICurrentTenant): a patient
// of another clinic is indistinguishable from none. The Clinic Admin recording
// the referral is the authenticated caller (ICurrentUser).
//
// Throws DuplicateReferralException when the referred patient was referred
// concurrently; nothing is saved in that case.
public sealed class CreateReferralHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly IReferralRepository _referralRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public CreateReferralHandler(
        IPatientRepository patientRepository,
        IReferralRepository referralRepository,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _patientRepository = patientRepository;
        _referralRepository = referralRepository;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<CreateReferralResult> HandleAsync(
        CreateReferralCommand command,
        CancellationToken cancellationToken = default)
    {
        // Fails closed like the other tenant-scoped patient use cases.
        if (!_currentTenant.HasTenant || !_currentUser.HasUser)
        {
            return CreateReferralResult.Rejected(CreateReferralStatus.PatientNotFound);
        }

        if (command.ReferrerPatientId == command.ReferredPatientId)
        {
            return CreateReferralResult.Rejected(CreateReferralStatus.SelfReferral);
        }

        var clinicId = _currentTenant.ClinicId!.Value;

        var referrer = await _patientRepository.GetByIdForTenantAsync(
            command.ReferrerPatientId,
            clinicId,
            cancellationToken);

        var referred = await _patientRepository.GetByIdForTenantAsync(
            command.ReferredPatientId,
            clinicId,
            cancellationToken);

        if (referrer is null || referred is null)
        {
            return CreateReferralResult.Rejected(CreateReferralStatus.PatientNotFound);
        }

        if (!referrer.IsActive || !referred.IsActive)
        {
            return CreateReferralResult.Rejected(CreateReferralStatus.PatientInactive);
        }

        // A repeated request is recognised before anything is written; the
        // database's unique key still stops one that races with it.
        if (await _referralRepository.ExistsForReferredPatientAsync(referred.Id, cancellationToken))
        {
            return CreateReferralResult.Rejected(CreateReferralStatus.AlreadyReferred);
        }

        var referral = Referral.Record(referrer, referred, _currentUser.UserId!.Value);

        await _referralRepository.AddAsync(referral, cancellationToken);

        return new CreateReferralResult(CreateReferralStatus.Created, referral.Id);
    }
}
