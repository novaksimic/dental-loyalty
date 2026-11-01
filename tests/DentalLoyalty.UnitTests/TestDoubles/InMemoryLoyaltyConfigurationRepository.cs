using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.TestDoubles;

public sealed class InMemoryLoyaltyConfigurationRepository : ILoyaltyConfigurationRepository
{
    private readonly Dictionary<Guid, LoyaltyConfiguration> _configurations = new();

    public int UpdateCount { get; private set; }

    public void Seed(LoyaltyConfiguration configuration)
    {
        _configurations[configuration.ClinicId] = configuration;
    }

    public Task<LoyaltyConfiguration?> GetByClinicIdAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        _configurations.TryGetValue(clinicId, out var configuration);
        return Task.FromResult(configuration);
    }

    public Task UpdateAsync(LoyaltyConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _configurations[configuration.ClinicId] = configuration;
        UpdateCount++;
        return Task.CompletedTask;
    }
}
