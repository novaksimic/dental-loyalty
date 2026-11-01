using DentalLoyalty.Application.Common.Interfaces;

namespace DentalLoyalty.Application.Patients.Commands;

public sealed class UpdatePatientContactHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentTenant _currentTenant;

    public UpdatePatientContactHandler(
        IPatientRepository patientRepository,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _currentTenant = currentTenant;
    }

    // Returns false both when the patient does not exist and when it belongs to
    // another clinic, so the caller cannot distinguish the two (maps to 404).
    // The tenant-scoped lookup itself is what prevents a Clinic B patient from
    // ever being reachable here, not a separate authorization check.
    public async Task<bool> HandleAsync(
        UpdatePatientContactCommand command,
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

        patient.UpdateContactInformation(command.PhoneNumber, command.Email);

        await _patientRepository.UpdateAsync(patient, cancellationToken);

        return true;
    }
}
