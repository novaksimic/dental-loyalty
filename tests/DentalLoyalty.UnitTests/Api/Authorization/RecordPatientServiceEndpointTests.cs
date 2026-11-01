using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// POST /api/patients/{id}/services through the real pipeline: ClinicAdmin of the
// patient's own clinic only (SECURITY.md §5.4 "Manage Points" = Own Clinic), with
// the clinic, price and points never taken from the request. Rejected calls
// verify that nothing was recorded.
public class RecordPatientServiceEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public RecordPatientServiceEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    // A clinic with its scoring configuration, a 7,500 RSD catalog service and a
    // registered, linked patient with an active cycle.
    private async Task<(Guid ClinicId, Guid ServiceId, Guid PatientId)> SeedAsync()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        var serviceId = await TestDataSeeder.SeedServiceAsync(_factory, clinicId, 7_500m);
        var (_, patientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Ana");
        return (clinicId, serviceId, patientId);
    }

    private HttpClient ClientFor(UserRole role, Guid? clinicId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateValidToken(role, clinicId));
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        Guid patientId,
        Guid serviceId,
        Guid? occurrenceId = null,
        DateTimeOffset? performedAt = null,
        object? extra = null) =>
        client.PostAsJsonAsync($"/api/patients/{patientId}/services", extra ?? new
        {
            id = occurrenceId ?? Guid.NewGuid(),
            serviceId,
            performedAt,
        });

    private async Task AssertNothingRecorded(Guid patientId)
    {
        Assert.Empty(await TestDataSeeder.GetPatientServicesAsync(_factory, patientId));
        Assert.Empty(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
        Assert.Equal(0, Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId)).Points);
    }

    [Fact]
    public async Task AsClinicAdmin_EligiblePatient_Returns201_AndCreditsPoints()
    {
        var (clinicId, serviceId, patientId) = await SeedAsync();
        var occurrenceId = Guid.NewGuid();

        var response = await PostAsync(ClientFor(UserRole.ClinicAdmin, clinicId), patientId, serviceId, occurrenceId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RecordedServiceResponse>();
        Assert.Equal(occurrenceId, body!.Id);
        Assert.True(body.PointsAwarded);
        Assert.Equal(75_000, body.EarnedPoints);
        Assert.Equal(75_000, body.CreditedPoints);

        var transaction = Assert.Single(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
        Assert.Equal(occurrenceId, transaction.PatientServiceId);
        Assert.Equal(75_000, Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId)).Points);
    }

    // The body cannot set the clinic, the price or the points.
    [Fact]
    public async Task ClientSuppliedClinicPriceOrPoints_AreIgnored()
    {
        var (clinicId, serviceId, patientId) = await SeedAsync();

        var response = await PostAsync(ClientFor(UserRole.ClinicAdmin, clinicId), patientId, serviceId, extra: new
        {
            id = Guid.NewGuid(),
            serviceId,
            clinicId = Guid.NewGuid(),
            priceAtTimeOfService = 1_000_000m,
            earnedPoints = 1_000_000,
            creditedPoints = 1_000_000,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var patientService = Assert.Single(await TestDataSeeder.GetPatientServicesAsync(_factory, patientId));
        Assert.Equal(clinicId, patientService.ClinicId);
        Assert.Equal(7_500m, patientService.PriceAtTimeOfService);
        Assert.Equal(75_000, Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId)).Points);
    }

    [Fact]
    public async Task UnlinkedPatient_Returns201_WithoutPoints()
    {
        var (clinicId, serviceId, _) = await SeedAsync();
        var patientId = await TestDataSeeder.SeedUnlinkedPatientAsync(_factory, clinicId);

        var response = await PostAsync(ClientFor(UserRole.ClinicAdmin, clinicId), patientId, serviceId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RecordedServiceResponse>();
        Assert.False(body!.PointsAwarded);
        Assert.Equal(0, body.CreditedPoints);
        Assert.Single(await TestDataSeeder.GetPatientServicesAsync(_factory, patientId));
        Assert.Empty(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
    }

    [Fact]
    public async Task RepeatedSubmissionOfTheSameOccurrence_Returns409_AndEarnsOnce()
    {
        var (clinicId, serviceId, patientId) = await SeedAsync();
        var client = ClientFor(UserRole.ClinicAdmin, clinicId);
        var occurrenceId = Guid.NewGuid();

        var first = await PostAsync(client, patientId, serviceId, occurrenceId);
        var second = await PostAsync(client, patientId, serviceId, occurrenceId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Single(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
        Assert.Equal(75_000, Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId)).Points);
    }

    [Fact]
    public async Task PatientOfAnotherClinic_Returns404_AndRecordsNothing()
    {
        var (_, _, otherPatientId) = await SeedAsync();
        var (ownClinicId, ownServiceId, _) = await SeedAsync();

        var response = await PostAsync(ClientFor(UserRole.ClinicAdmin, ownClinicId), otherPatientId, ownServiceId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingRecorded(otherPatientId);
    }

    [Fact]
    public async Task ServiceOfAnotherClinic_Returns404_AndRecordsNothing()
    {
        var (_, otherServiceId, _) = await SeedAsync();
        var (clinicId, _, patientId) = await SeedAsync();

        var response = await PostAsync(ClientFor(UserRole.ClinicAdmin, clinicId), patientId, otherServiceId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingRecorded(patientId);
    }

    [Fact]
    public async Task InactiveCatalogService_Returns409_AndRecordsNothing()
    {
        var (clinicId, _, patientId) = await SeedAsync();
        var inactiveServiceId = await TestDataSeeder.SeedServiceAsync(_factory, clinicId, 2_000m, active: false);

        var response = await PostAsync(ClientFor(UserRole.ClinicAdmin, clinicId), patientId, inactiveServiceId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNothingRecorded(patientId);
    }

    [Fact]
    public async Task PerformanceTimeInTheFuture_Returns400_AndRecordsNothing()
    {
        var (clinicId, serviceId, patientId) = await SeedAsync();

        var response = await PostAsync(
            ClientFor(UserRole.ClinicAdmin, clinicId), patientId, serviceId, performedAt: DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingRecorded(patientId);
    }

    [Fact]
    public async Task WithoutToken_Returns401_AndRecordsNothing()
    {
        var (_, serviceId, patientId) = await SeedAsync();

        var response = await PostAsync(_factory.CreateClient(), patientId, serviceId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertNothingRecorded(patientId);
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.PlatformAdmin)]
    public async Task AsOtherRole_Returns403_AndRecordsNothing(UserRole role)
    {
        var (clinicId, serviceId, patientId) = await SeedAsync();

        var response = await PostAsync(
            ClientFor(role, role == UserRole.PlatformAdmin ? null : clinicId), patientId, serviceId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingRecorded(patientId);
    }

    // The right role but no tenant claim must not widen access - it fails closed.
    [Fact]
    public async Task ClinicAdminTokenWithoutTenantClaim_Returns404_AndRecordsNothing()
    {
        var (_, serviceId, patientId) = await SeedAsync();

        var response = await PostAsync(ClientFor(UserRole.ClinicAdmin, clinicId: null), patientId, serviceId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingRecorded(patientId);
    }

    private sealed record RecordedServiceResponse(Guid Id, bool PointsAwarded, int EarnedPoints, int CreditedPoints);
}
