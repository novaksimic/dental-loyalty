using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Patients.Commands;

public sealed class DeactivatePatientHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentTenant _currentTenant;

    public DeactivatePatientHandler(
        IPatientRepository patientRepository,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _currentTenant = currentTenant;
    }

    // Deactivation (not a real row delete - SECURITY.md §7.10 requires patient
    // deletion to be controlled given dependent loyalty history/referrals/rewards).
    // Same not-found-vs-other-tenant behavior as GetPatientByIdHandler.
    public async Task<bool> HandleAsync(
        DeactivatePatientCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return false;
        }

        var patient = await _patientRepository.GetByIdForTenantAsync(
            command.PatientId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);

        if (patient is null)
        {
            return false;
        }

        patient.Deactivate();

        await _patientRepository.UpdateAsync(patient, cancellationToken);

        return true;
    }
}
