using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Application.Loyalty;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Patients.Commands;

// Records a completed service for a patient of the caller's own clinic and
// credits the loyalty points it earns (BUSINESS.md §3, §5.1, §14).
//
// Every lookup is scoped to the authenticated tenant (ICurrentTenant): a patient
// or catalog service of another clinic is indistinguishable from none. The user
// responsible is the authenticated caller (ICurrentUser).
//
// A service earns points only when the patient's current cycle accepts it
// (LoyaltyCycle.AcceptsService). Otherwise it is recorded for clinic history
// with no points and no loyalty transaction - including a service performed in
// a cycle that has since ended (ADR-008). Either way the occurrence, and for
// points also the account change and its transaction, are saved in one database
// transaction.
//
// The cycle lifecycle is applied through LoyaltyCycleProgression (ADR-008): an
// expired cycle is ended and the next one started before the service is
// recorded, and points that satisfy the cycle's last completion condition
// complete it in the same database transaction as the points.
//
// Throws ConcurrencyConflictException when the account changed concurrently and
// DuplicateServiceOccurrenceException when the occurrence was already recorded;
// nothing is saved in either case.
public sealed class RecordPatientServiceHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly IServiceRepository _serviceRepository;
    private readonly ILoyaltyConfigurationRepository _loyaltyConfigurationRepository;
    private readonly ILoyaltyAccountRepository _loyaltyAccountRepository;
    private readonly LoyaltyCycleProgression _loyaltyCycleProgression;
    private readonly IPatientServiceRepository _patientServiceRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public RecordPatientServiceHandler(
        IPatientRepository patientRepository,
        IServiceRepository serviceRepository,
        ILoyaltyConfigurationRepository loyaltyConfigurationRepository,
        ILoyaltyAccountRepository loyaltyAccountRepository,
        LoyaltyCycleProgression loyaltyCycleProgression,
        IPatientServiceRepository patientServiceRepository,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _patientRepository = patientRepository;
        _serviceRepository = serviceRepository;
        _loyaltyConfigurationRepository = loyaltyConfigurationRepository;
        _loyaltyAccountRepository = loyaltyAccountRepository;
        _loyaltyCycleProgression = loyaltyCycleProgression;
        _patientServiceRepository = patientServiceRepository;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<RecordPatientServiceResult> HandleAsync(
        RecordPatientServiceCommand command,
        CancellationToken cancellationToken = default)
    {
        // Fails closed like the other tenant-scoped patient use cases: without a
        // trusted tenant and user there is no patient this caller can reach.
        if (!_currentTenant.HasTenant || !_currentUser.HasUser)
        {
            return RecordPatientServiceResult.Rejected(RecordPatientServiceStatus.PatientNotFound);
        }

        var clinicId = _currentTenant.ClinicId!.Value;
        var recordedBy = _currentUser.UserId!.Value;
        var now = DateTime.UtcNow;
        var performedAt = command.PerformedAt ?? now;

        if (command.PatientServiceId == Guid.Empty
            || performedAt.Kind != DateTimeKind.Utc
            || performedAt > now)
        {
            return RecordPatientServiceResult.Rejected(RecordPatientServiceStatus.InvalidRequest);
        }

        var patient = await _patientRepository.GetByIdForTenantAsync(
            command.PatientId,
            clinicId,
            cancellationToken);

        if (patient is null)
        {
            return RecordPatientServiceResult.Rejected(RecordPatientServiceStatus.PatientNotFound);
        }

        var service = await _serviceRepository.GetByIdForTenantAsync(
            command.ServiceId,
            clinicId,
            cancellationToken);

        if (service is null)
        {
            return RecordPatientServiceResult.Rejected(RecordPatientServiceStatus.ServiceNotFound);
        }

        if (!patient.IsActive)
        {
            return RecordPatientServiceResult.Rejected(RecordPatientServiceStatus.PatientInactive);
        }

        if (!service.IsActive)
        {
            return RecordPatientServiceResult.Rejected(RecordPatientServiceStatus.ServiceInactive);
        }

        // A repeated submission of an occurrence is recognised before anything is
        // written; the database's unique key still stops one that races with it.
        if (await _patientServiceRepository.ExistsAsync(command.PatientServiceId, cancellationToken))
        {
            return RecordPatientServiceResult.Rejected(RecordPatientServiceStatus.AlreadyRecorded);
        }

        // Every clinic is created with its scoring configuration (BUSINESS.md §3.1).
        var scoring = await _loyaltyConfigurationRepository.GetByClinicIdAsync(
            clinicId,
            cancellationToken)
            ?? throw new InvalidOperationException("The clinic has no scoring configuration.");

        // ADR-006: every Patient has its account from the moment it is created.
        var account = await _loyaltyAccountRepository.GetByPatientIdForTenantAsync(
            patient.Id,
            clinicId,
            cancellationToken)
            ?? throw new InvalidOperationException("The patient has no loyalty account.");

        var currentCycle = await _loyaltyCycleProgression.GetCurrentCycleAsync(
            account,
            now,
            save: true,
            cancellationToken);

        // One instant for the whole operation: the cycle was brought up to date
        // for now, so the service is recorded, accepted and credited at now.
        var patientService = new PatientService(
            command.PatientServiceId,
            patient,
            service,
            scoring,
            currentCycle,
            performedAt,
            recordedBy,
            recordedAt: now);

        if (currentCycle is null || patient.UserId is null || !currentCycle.AcceptsService(patientService))
        {
            await _patientServiceRepository.AddAsync(patientService, cancellationToken);

            return new RecordPatientServiceResult(
                RecordPatientServiceStatus.RecordedWithoutPoints,
                patientService.Id);
        }

        var transaction = account.EarnServicePoints(patientService, currentCycle);

        // A completion is recorded at the time of the transaction that caused
        // it, so the reset never precedes it in history (as for redemptions).
        var completion = await _loyaltyCycleProgression.CompleteIfSatisfiedAsync(
            account,
            currentCycle,
            transaction.CreatedAt,
            cancellationToken: cancellationToken);

        await _loyaltyAccountRepository.SaveServicePointsAsync(
            patientService,
            account,
            transaction,
            cancellationToken);

        return new RecordPatientServiceResult(
            RecordPatientServiceStatus.RecordedWithPoints,
            patientService.Id,
            patientService.EarnedPoints,
            transaction.PointsChange,
            CycleCompleted: completion is not null);
    }
}
