using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Loyalty;

// The handlers take no clinic id at all: the tenant context is the only input
// that decides which clinic's configuration is read or changed.
public class LoyaltyConfigurationHandlerTests
{
    private readonly Guid _clinicAId = Guid.NewGuid();
    private readonly Guid _clinicBId = Guid.NewGuid();
    private readonly InMemoryLoyaltyConfigurationRepository _repository = new();

    public LoyaltyConfigurationHandlerTests()
    {
        _repository.Seed(LoyaltyConfiguration.CreateDefault(_clinicAId));
        _repository.Seed(new LoyaltyConfiguration(_clinicBId, 50m, 250));
    }

    [Fact]
    public async Task Get_WithTenant_ReturnsOwnClinicsConfiguration()
    {
        var handler = new GetLoyaltyConfigurationHandler(_repository, FakeCurrentTenant.ForClinic(_clinicBId));

        var configuration = await handler.HandleAsync(new GetLoyaltyConfigurationQuery());

        Assert.NotNull(configuration);
        Assert.Equal(_clinicBId, configuration!.ClinicId);
        Assert.Equal(50m, configuration.SpendAmountRsd);
        Assert.Equal(250, configuration.PointsAwarded);
    }

    [Fact]
    public async Task Get_WithoutTenant_ReturnsNull()
    {
        var handler = new GetLoyaltyConfigurationHandler(_repository, FakeCurrentTenant.NoTenant());

        Assert.Null(await handler.HandleAsync(new GetLoyaltyConfigurationQuery()));
    }

    [Fact]
    public async Task Get_ForTenantWithoutConfiguration_ReturnsNull()
    {
        var handler = new GetLoyaltyConfigurationHandler(_repository, FakeCurrentTenant.ForClinic(Guid.NewGuid()));

        Assert.Null(await handler.HandleAsync(new GetLoyaltyConfigurationQuery()));
    }

    [Fact]
    public async Task Update_WithTenant_ChangesOnlyOwnClinicsConfiguration()
    {
        var handler = new UpdateLoyaltyConfigurationHandler(_repository, FakeCurrentTenant.ForClinic(_clinicAId));

        var updated = await handler.HandleAsync(new UpdateLoyaltyConfigurationCommand(200m, 3_000));

        Assert.True(updated);

        var clinicA = await _repository.GetByClinicIdAsync(_clinicAId);
        Assert.Equal(200m, clinicA!.SpendAmountRsd);
        Assert.Equal(3_000, clinicA.PointsAwarded);

        var clinicB = await _repository.GetByClinicIdAsync(_clinicBId);
        Assert.Equal(50m, clinicB!.SpendAmountRsd);
        Assert.Equal(250, clinicB.PointsAwarded);
    }

    [Fact]
    public async Task Update_WithoutTenant_ReturnsFalseAndChangesNothing()
    {
        var handler = new UpdateLoyaltyConfigurationHandler(_repository, FakeCurrentTenant.NoTenant());

        var updated = await handler.HandleAsync(new UpdateLoyaltyConfigurationCommand(200m, 3_000));

        Assert.False(updated);
        Assert.Equal(0, _repository.UpdateCount);
    }

    [Fact]
    public async Task Update_ForTenantWithoutConfiguration_ReturnsFalse()
    {
        var handler = new UpdateLoyaltyConfigurationHandler(_repository, FakeCurrentTenant.ForClinic(Guid.NewGuid()));

        var updated = await handler.HandleAsync(new UpdateLoyaltyConfigurationCommand(200m, 3_000));

        Assert.False(updated);
        Assert.Equal(0, _repository.UpdateCount);
    }

    [Fact]
    public async Task Update_WithInvalidRatio_IsRejectedAndNothingSaved()
    {
        var handler = new UpdateLoyaltyConfigurationHandler(_repository, FakeCurrentTenant.ForClinic(_clinicAId));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handler.HandleAsync(new UpdateLoyaltyConfigurationCommand(0m, 1_000)));

        Assert.Equal(0, _repository.UpdateCount);
        var clinicA = await _repository.GetByClinicIdAsync(_clinicAId);
        Assert.Equal(100m, clinicA!.SpendAmountRsd);
        Assert.Equal(1_000, clinicA.PointsAwarded);
    }
}
