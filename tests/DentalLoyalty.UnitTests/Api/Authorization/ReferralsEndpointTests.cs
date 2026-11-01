using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// POST /api/referrals and POST /api/referrals/{id}/validation through the real
// pipeline: ClinicAdmin of the patients' own clinic only (SECURITY.md §5.4
// "Manage Referrals" = Own Clinic; ADR-007), with rewards always decided by the
// backend. Rejected calls verify that nothing was recorded.
public class ReferralsEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public ReferralsEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    // A clinic with two registered, linked patients.
    private async Task<(Guid ClinicId, Guid ReferrerId, Guid ReferredId)> SeedAsync()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        var (_, referrerId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Ana");
        var (_, referredId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Marko");
        return (clinicId, referrerId, referredId);
    }

    private HttpClient ClientFor(UserRole role, Guid? clinicId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateValidToken(role, clinicId));
        return client;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, Guid referrerId, Guid referredId) =>
        client.PostAsJsonAsync("/api/referrals", new { referrerPatientId = referrerId, referredPatientId = referredId });

    private static Task<HttpResponseMessage> ValidateAsync(HttpClient client, Guid referralId, DateTimeOffset? examinedAt = null) =>
        examinedAt is null
            ? client.PostAsync($"/api/referrals/{referralId}/validation", content: null)
            : client.PostAsJsonAsync($"/api/referrals/{referralId}/validation", new { examinedAt });

    private async Task<Guid> CreateReferralAsync(Guid clinicId, Guid referrerId, Guid referredId)
    {
        var response = await CreateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referrerId, referredId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.Id;
    }

    private async Task AssertNotRewarded(params Guid[] patientIds)
    {
        foreach (var patientId in patientIds)
        {
            Assert.Empty(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
            var account = Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId));
            Assert.Equal((0, 0), (account.Points, account.ReferralBalance));
        }
    }

    // --- create ---------------------------------------------------------------

    [Fact]
    public async Task Create_AsClinicAdmin_Returns201_AndRecordsPendingReferral()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();

        var response = await CreateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referrerId, referredId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        Assert.Equal("Pending", body!.Status);
        var referral = Assert.Single(await TestDataSeeder.GetReferralsAsync(_factory, referredId));
        Assert.Equal((body.Id, referrerId, clinicId), (referral.Id, referral.ReferrerPatientId, referral.ClinicId));
        await AssertNotRewarded(referrerId, referredId);
    }

    [Fact]
    public async Task Create_SelfReferral_Returns400()
    {
        var (clinicId, referrerId, _) = await SeedAsync();

        var response = await CreateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referrerId, referrerId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await TestDataSeeder.GetReferralsAsync(_factory, referrerId));
    }

    [Fact]
    public async Task Create_ForAnAlreadyReferredPatient_Returns409()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        await CreateReferralAsync(clinicId, referrerId, referredId);

        var repeated = await CreateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referrerId, referredId);

        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Single(await TestDataSeeder.GetReferralsAsync(_factory, referredId));
    }

    [Fact]
    public async Task Create_WithAPatientOfAnotherClinic_Returns404()
    {
        var (clinicId, referrerId, _) = await SeedAsync();
        var otherClinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 100m, 1_000);
        var (_, otherPatientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, otherClinicId, "Iva");

        var response = await CreateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referrerId, otherPatientId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await TestDataSeeder.GetReferralsAsync(_factory, otherPatientId));
    }

    // --- validate -------------------------------------------------------------

    [Fact]
    public async Task Validate_AsClinicAdmin_Returns200_AndRewardsBothPatients()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        var referralId = await CreateReferralAsync(clinicId, referrerId, referredId);

        var response = await ValidateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referralId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidatedResponse>();
        Assert.Equal("Completed", body!.Status);
        Assert.Equal(new RewardResponse("Rewarded", 50_000), body.Referrer);
        Assert.Equal(new RewardResponse("Rewarded", 50_000), body.Referred);

        foreach (var patientId in new[] { referrerId, referredId })
        {
            var account = Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId));
            Assert.Equal((50_000, 1), (account.Points, account.ReferralBalance));
            var transaction = Assert.Single(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
            Assert.Equal(LoyaltyTransactionType.ReferralPointsEarned, transaction.Type);
            Assert.Equal((50_000, 1, referralId), (transaction.PointsChange, transaction.ReferralChange, transaction.ReferralId));
        }
    }

    [Fact]
    public async Task Validate_WithUnlinkedReferredPatient_Returns200_AndForfeitsForBoth()
    {
        var (clinicId, referrerId, _) = await SeedAsync();
        var unlinkedId = await TestDataSeeder.SeedUnlinkedPatientAsync(_factory, clinicId);
        var referralId = await CreateReferralAsync(clinicId, referrerId, unlinkedId);

        var response = await ValidateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referralId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ValidatedResponse>();
        Assert.Equal(new RewardResponse("Forfeited", 0), body!.Referrer);
        Assert.Equal(new RewardResponse("Forfeited", 0), body.Referred);
        Assert.True(Assert.Single(await TestDataSeeder.GetReferralsAsync(_factory, unlinkedId)).RewardsForfeited);
        await AssertNotRewarded(referrerId, unlinkedId);
    }

    [Fact]
    public async Task Validate_Repeated_Returns409_AndRewardsOnce()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        var referralId = await CreateReferralAsync(clinicId, referrerId, referredId);
        var client = ClientFor(UserRole.ClinicAdmin, clinicId);

        await ValidateAsync(client, referralId);
        var repeated = await ValidateAsync(client, referralId);

        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Single(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, referrerId));
        Assert.Single(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, referredId));
    }

    [Fact]
    public async Task Validate_ExaminationInTheFuture_Returns400_AndRewardsNothing()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        var referralId = await CreateReferralAsync(clinicId, referrerId, referredId);

        var response = await ValidateAsync(ClientFor(UserRole.ClinicAdmin, clinicId), referralId, DateTimeOffset.UtcNow.AddHours(1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNotRewarded(referrerId, referredId);
    }

    [Fact]
    public async Task Validate_ReferralOfAnotherClinic_Returns404_AndRewardsNothing()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        var referralId = await CreateReferralAsync(clinicId, referrerId, referredId);
        var otherClinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 100m, 1_000);

        var response = await ValidateAsync(ClientFor(UserRole.ClinicAdmin, otherClinicId), referralId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNotRewarded(referrerId, referredId);
    }

    // --- authentication and roles ---------------------------------------------

    [Fact]
    public async Task WithoutToken_Returns401_AndRecordsNothing()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        var referralId = await CreateReferralAsync(clinicId, referrerId, referredId);
        var (_, otherId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Iva");

        var create = await CreateAsync(_factory.CreateClient(), referrerId, otherId);
        var validate = await ValidateAsync(_factory.CreateClient(), referralId);

        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, validate.StatusCode);
        Assert.Empty(await TestDataSeeder.GetReferralsAsync(_factory, otherId));
        await AssertNotRewarded(referrerId, referredId);
    }

    // Patients cannot change their referral count (DATA.md §10, SECURITY.md).
    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.PlatformAdmin)]
    public async Task AsOtherRole_Returns403_AndRecordsNothing(UserRole role)
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        var referralId = await CreateReferralAsync(clinicId, referrerId, referredId);
        var (_, otherId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Iva");
        var client = ClientFor(role, role == UserRole.PlatformAdmin ? null : clinicId);

        var create = await CreateAsync(client, referrerId, otherId);
        var validate = await ValidateAsync(client, referralId);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, validate.StatusCode);
        Assert.Empty(await TestDataSeeder.GetReferralsAsync(_factory, otherId));
        await AssertNotRewarded(referrerId, referredId);
    }

    // The right role but no tenant claim must not widen access - it fails closed.
    [Fact]
    public async Task ClinicAdminTokenWithoutTenantClaim_Returns404_AndRecordsNothing()
    {
        var (clinicId, referrerId, referredId) = await SeedAsync();
        var referralId = await CreateReferralAsync(clinicId, referrerId, referredId);
        var (_, otherId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Iva");
        var client = ClientFor(UserRole.ClinicAdmin, clinicId: null);

        Assert.Equal(HttpStatusCode.NotFound, (await CreateAsync(client, referrerId, otherId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ValidateAsync(client, referralId)).StatusCode);
        Assert.Empty(await TestDataSeeder.GetReferralsAsync(_factory, otherId));
        await AssertNotRewarded(referrerId, referredId);
    }

    private sealed record CreatedResponse(Guid Id, string Status);

    private sealed record ValidatedResponse(Guid Id, string Status, RewardResponse Referrer, RewardResponse Referred);

    private sealed record RewardResponse(string Outcome, int CreditedPoints);
}
