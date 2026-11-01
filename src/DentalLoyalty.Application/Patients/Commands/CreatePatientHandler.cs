using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.Application.Patients.Commands;

public sealed class CreatePatientHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentTenant _currentTenant;

    public CreatePatientHandler(
        IPatientRepository patientRepository,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _currentTenant = currentTenant;
    }

    public async Task<Guid> HandleAsync(
        CreatePatientCommand command,
        CancellationToken cancellationToken = default)
    {
        // A caller operating within a tenant (ClinicAdmin) can only ever create
        // patients in their own clinic - the authenticated tenant is authoritative
        // and command.ClinicId is ignored, regardless of what the client sent.
        // A caller with no tenant of their own (PlatformAdmin - the only role a
        // token is issued for without a "clinicId" claim) is performing a
        // platform-level operation and may target any clinic explicitly via
        // command.ClinicId.
        var clinicId = _currentTenant.HasTenant
            ? _currentTenant.ClinicId!.Value
            : command.ClinicId;

        var patient = new Patient(
            clinicId,
            command.FirstName,
            command.LastName,
            command.PhoneNumber,
            command.Email);

        // ADR-006: the account is created with the Patient, in the same save. It
        // starts at 0 points; no loyalty cycle starts until the patient registers
        // and links their user account (BUSINESS.md §5.1).
        var loyaltyAccount = LoyaltyAccount.OpenFor(patient);

        await _patientRepository.AddAsync(
            patient,
            loyaltyAccount,
            cancellationToken);

        return patient.Id;
    }
}