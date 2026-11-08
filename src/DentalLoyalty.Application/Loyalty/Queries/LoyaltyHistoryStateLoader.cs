using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Loyalty;

namespace DentalLoyalty.Application.Loyalty.Queries;

// The patient whose history is read, their account and their cycle as of now.
// PendingTransitions are the expiries the next operation will record (ADR-008
// decision 6), already applied to Account and CurrentCycle but never saved.
public sealed record LoyaltyHistoryState(
    Guid PatientId,
    Guid ClinicId,
    LoyaltyAccount Account,
    LoyaltyCycle? CurrentCycle,
    IReadOnlyList<CycleTransition> PendingTransitions,
    DateTime Now);

// Resolves a LoyaltyHistorySubject within the caller's tenant and loads the
// state both history views start from. Returns null - so the caller answers 404
// - when there is no trusted tenant (or, for CurrentPatient, no trusted user),
// or no such patient in the caller's clinic: a patient of another clinic is
// indistinguishable from none.
public sealed class LoyaltyHistoryStateLoader
{
    private readonly IPatientRepository _patientRepository;
    private readonly ILoyaltyAccountRepository _loyaltyAccountRepository;
    private readonly LoyaltyCycleProgression _loyaltyCycleProgression;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public LoyaltyHistoryStateLoader(
        IPatientRepository patientRepository,
        ILoyaltyAccountRepository loyaltyAccountRepository,
        LoyaltyCycleProgression loyaltyCycleProgression,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _loyaltyAccountRepository = loyaltyAccountRepository;
        _loyaltyCycleProgression = loyaltyCycleProgression;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public async Task<LoyaltyHistoryState?> LoadAsync(
        LoyaltyHistorySubject subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        var clinicId = _currentTenant.ClinicId!.Value;

        Patient? patient;

        if (subject.PatientId is Guid patientId)
        {
            patient = await _patientRepository.GetByIdForTenantAsync(patientId, clinicId, cancellationToken);
        }
        else
        {
            if (!_currentUser.HasUser)
            {
                return null;
            }

            patient = await _patientRepository.GetByUserIdForTenantAsync(
                _currentUser.UserId!.Value,
                clinicId,
                cancellationToken);
        }

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

        var cycle = await _loyaltyCycleProgression.ViewCurrentCycleAsync(account, now, cancellationToken);

        return new LoyaltyHistoryState(
            patient.Id,
            clinicId,
            account,
            cycle.Current,
            cycle.PendingTransitions,
            now);
    }

    // Times are stored and compared as UTC; values read back from the database
    // carry no kind, so they are marked as UTC before they are returned.
    internal static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    internal static DateTime? AsUtc(DateTime? value) => value is DateTime v ? AsUtc(v) : null;
}
