using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.TestDoubles;

public sealed class InMemoryPatientRepository : IPatientRepository
{
    private readonly Dictionary<Guid, Patient> _patients = new();
    private readonly InMemoryUserRepository? _users;

    // Pass the user repository the handler reads from so users saved through
    // LinkUserAsync are visible to its lookups, as with a shared DbContext.
    public InMemoryPatientRepository(InMemoryUserRepository? users = null)
    {
        _users = users;
    }

    public List<LoyaltyAccount> LoyaltyAccounts { get; } = new();

    public List<User> LinkedUsers { get; } = new();

    public List<LoyaltyCycle> LoyaltyCycles { get; } = new();

    public void Seed(Patient patient)
    {
        _patients[patient.Id] = patient;
    }

    public Task AddAsync(Patient patient, LoyaltyAccount loyaltyAccount, CancellationToken cancellationToken = default)
    {
        _patients[patient.Id] = patient;
        LoyaltyAccounts.Add(loyaltyAccount);
        return Task.CompletedTask;
    }

    public async Task LinkUserAsync(Patient patient, User user, LoyaltyCycle firstCycle, CancellationToken cancellationToken = default)
    {
        _patients[patient.Id] = patient;
        LinkedUsers.Add(user);
        LoyaltyCycles.Add(firstCycle);

        if (_users is not null)
        {
            await _users.AddAsync(user, cancellationToken);
        }
    }

    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _patients.TryGetValue(id, out var patient);
        return Task.FromResult(patient);
    }

    public Task<Patient?> GetByIdForTenantAsync(Guid id, Guid clinicId, CancellationToken cancellationToken = default)
    {
        _patients.TryGetValue(id, out var patient);
        return Task.FromResult(patient is not null && patient.ClinicId == clinicId ? patient : null);
    }

    public Task<Patient?> GetByIdForPatientOwnerAsync(Guid id, Guid clinicId, Guid userId, CancellationToken cancellationToken = default)
    {
        _patients.TryGetValue(id, out var patient);
        return Task.FromResult(
            patient is not null && patient.ClinicId == clinicId && patient.UserId == userId
                ? patient
                : null);
    }

    public Task<Patient?> GetByUserIdForTenantAsync(Guid userId, Guid clinicId, CancellationToken cancellationToken = default)
    {
        var patient = _patients.Values.SingleOrDefault(p => p.UserId == userId && p.ClinicId == clinicId);
        return Task.FromResult(patient);
    }

    public Task UpdateAsync(Patient patient, CancellationToken cancellationToken = default)
    {
        _patients[patient.Id] = patient;
        return Task.CompletedTask;
    }
}
