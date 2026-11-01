using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Patients.Queries;

public sealed class GetPatientByIdHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetPatientByIdHandler(
        IPatientRepository patientRepository,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _currentTenant = currentTenant;
    }

    // Returns null both when the patient does not exist and when it belongs to
    // another clinic - the caller must not be able to tell the two apart.
    public async Task<Patient?> HandleAsync(
        GetPatientByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return null;
        }

        return await _patientRepository.GetByIdForTenantAsync(
            query.PatientId,
            _currentTenant.ClinicId!.Value,
            cancellationToken);
    }
}
