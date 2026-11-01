using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.TestDoubles;

public sealed class InMemoryClinicRepository : IClinicRepository
{
    public List<Clinic> Clinics { get; } = new();

    public List<LoyaltyConfiguration> LoyaltyConfigurations { get; } = new();

    public Task AddAsync(
        Clinic clinic,
        LoyaltyConfiguration loyaltyConfiguration,
        CancellationToken cancellationToken = default)
    {
        Clinics.Add(clinic);
        LoyaltyConfigurations.Add(loyaltyConfiguration);
        return Task.CompletedTask;
    }
}
