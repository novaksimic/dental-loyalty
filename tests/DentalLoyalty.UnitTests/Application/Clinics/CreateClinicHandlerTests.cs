using DentalLoyalty.Application.Clinics.Commands;
using DentalLoyalty.UnitTests.TestDoubles;
using Xunit;

namespace DentalLoyalty.UnitTests.Application.Clinics;

public class CreateClinicHandlerTests
{
    // BUSINESS.md §3.1 / SECURITY.md §5.1: every new clinic starts with the
    // platform default scoring configuration, 100 RSD = 1,000 points.
    [Fact]
    public async Task HandleAsync_ProvisionsDefaultScoringConfigurationForTheNewClinic()
    {
        var repository = new InMemoryClinicRepository();
        var handler = new CreateClinicHandler(repository);

        var clinicId = await handler.HandleAsync(new CreateClinicCommand("Clinic A"));

        var clinic = Assert.Single(repository.Clinics);
        var configuration = Assert.Single(repository.LoyaltyConfigurations);
        Assert.Equal(clinicId, clinic.Id);
        Assert.Equal(clinicId, configuration.ClinicId);
        Assert.Equal(100m, configuration.SpendAmountRsd);
        Assert.Equal(1_000, configuration.PointsAwarded);
    }
}
