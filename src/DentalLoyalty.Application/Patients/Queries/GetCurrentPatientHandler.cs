using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Patients.Queries;

// Patient self-service: resolves the Patient record linked to the authenticated
// User within the authenticated tenant (SECURITY.md §5.4 "Own Profile").
public sealed class GetCurrentPatientHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public GetCurrentPatientHandler(
        IPatientRepository patientRepository,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    // Returns null when there is no trusted user or tenant context, or when no
    // Patient in the caller's tenant is linked to the caller's User.
    public async Task<Patient?> HandleAsync(
        GetCurrentPatientQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.HasUser || !_currentTenant.HasTenant)
        {
            return null;
        }

        return await _patientRepository.GetByUserIdForTenantAsync(
            _currentUser.UserId!.Value,
            _currentTenant.ClinicId!.Value,
            cancellationToken);
    }
}
