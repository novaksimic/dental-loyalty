using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// GET /api/patients/{id}/rewards and POST /api/patients/{id}/rewards/{rewardId}/redemptions
// through the real pipeline: ClinicAdmin of the patient's own clinic only
// (SECURITY.md §5.4 "Redeem Reward" = Own Clinic), with eligibility always
// evaluated by the backend. Rejected calls verify that nothing was recorded.
public class PatientRewardsEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public PatientRewardsEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    // A clinic with a registered, linked patient holding 650,000 points and 3
    // referral credits, and two rewards: Tier 1 (unlocked) and Tier 3 (locked).
    private async Task<(Guid ClinicId, Guid PatientId, Guid Tier1RewardId, Guid Tier3RewardId)> SeedAsync()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        var (_, patientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Ana");
        await TestDataSeeder.SetLoyaltyBalancesAsync(_factory, patientId, 650_000, 3);
        var tier1 = await TestDataSeeder.SeedRewardAsync(_factory, clinicId, "Peskiranje", 300_000, 1);
        var tier3 = await TestDataSeeder.SeedRewardAsync(_factory, clinicId, "Kućno izbeljivanje zuba", 1_000_000, 5);
        return (clinicId, patientId, tier1, tier3);
    }

    private HttpClient ClientFor(UserRole role, Guid? clinicId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateValidToken(role, clinicId));
        return client;
    }

    private static Task<HttpResponseMessage> RedeemAsync(HttpClient client, Guid patientId, Guid rewardId) =>
        client.PostAsync($"/api/patients/{patientId}/rewards/{rewardId}/redemptions", content: null);

    private async Task AssertNothingRedeemed(Guid patientId)
    {
        Assert.Empty(await TestDataSeeder.GetRewardRedemptionsAsync(_factory, patientId));
        Assert.Empty(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
        var account = Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId));
        Assert.Equal((650_000, 3), (account.Points, account.ReferralBalance));
    }

    [Fact]
    public async Task GetRewards_AsClinicAdmin_ReturnsBalancesAndEachRewardsStatus()
    {
        var (clinicId, patientId, _, _) = await SeedAsync();

        var response = await ClientFor(UserRole.ClinicAdmin, clinicId).GetAsync($"/api/patients/{patientId}/rewards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RewardsResponse>();
        Assert.Equal(650_000, body!.Points);
        Assert.Equal(3, body.ReferralBalance);
        Assert.Equal(
            new[] { ("Peskiranje", "Available"), ("Kućno izbeljivanje zuba", "Locked") },
            body.Rewards.Select(r => (r.Name, r.Status)));
    }

    [Fact]
    public async Task Redeem_AsClinicAdmin_Returns201_AndDeductsNothing()
    {
        var (clinicId, patientId, tier1, _) = await SeedAsync();
        var client = ClientFor(UserRole.ClinicAdmin, clinicId);

        var response = await RedeemAsync(client, patientId, tier1);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var redemption = Assert.Single(await TestDataSeeder.GetRewardRedemptionsAsync(_factory, patientId));
        Assert.Equal(tier1, redemption.RewardId);
        var transaction = Assert.Single(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
        Assert.Equal(LoyaltyTransactionType.RewardRedeemed, transaction.Type);
        Assert.Equal((0, 0), (transaction.PointsChange, transaction.ReferralChange));
        var account = Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId));
        Assert.Equal((650_000, 3), (account.Points, account.ReferralBalance));

        var rewards = await (await client.GetAsync($"/api/patients/{patientId}/rewards")).Content.ReadFromJsonAsync<RewardsResponse>();
        Assert.Equal("Redeemed", Assert.Single(rewards!.Rewards, r => r.Id == tier1).Status);
    }

    // BUSINESS.md §5.2, §19 step 5: redeeming the last unlocked reward at the
    // targets completes the cycle in the same request; the next one starts from
    // zero.
    [Fact]
    public async Task Redeem_TheLastUnlockedReward_CompletesTheCycle()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        var (_, patientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Ana");
        await TestDataSeeder.SetLoyaltyBalancesAsync(_factory, patientId, 1_000_000, 5);
        var tier3 = await TestDataSeeder.SeedRewardAsync(_factory, clinicId, "Kućno izbeljivanje zuba", 1_000_000, 5);
        var client = ClientFor(UserRole.ClinicAdmin, clinicId);

        var response = await RedeemAsync(client, patientId, tier3);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<RedeemedResponse>())!.CycleCompleted);
        var cycles = await TestDataSeeder.GetLoyaltyCyclesAsync(_factory, patientId);
        Assert.Equal(
            new[] { LoyaltyCycleStatus.Completed, LoyaltyCycleStatus.Active },
            cycles.OrderBy(c => c.StartedAt).Select(c => c.Status));
        var view = await (await client.GetAsync($"/api/patients/{patientId}/rewards")).Content.ReadFromJsonAsync<RewardsResponse>();
        Assert.Equal((0, 0), (view!.Points, view.ReferralBalance));
        Assert.Equal("Locked", Assert.Single(view.Rewards).Status);
    }

    [Fact]
    public async Task Redeem_RepeatedInTheSameCycle_Returns409_AndRedeemsOnce()
    {
        var (clinicId, patientId, tier1, _) = await SeedAsync();
        var client = ClientFor(UserRole.ClinicAdmin, clinicId);

        await RedeemAsync(client, patientId, tier1);
        var repeated = await RedeemAsync(client, patientId, tier1);

        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Single(await TestDataSeeder.GetRewardRedemptionsAsync(_factory, patientId));
        Assert.Single(await TestDataSeeder.GetLoyaltyTransactionsAsync(_factory, patientId));
    }

    [Fact]
    public async Task Redeem_LockedReward_Returns409_AndRedeemsNothing()
    {
        var (clinicId, patientId, _, tier3) = await SeedAsync();

        var response = await RedeemAsync(ClientFor(UserRole.ClinicAdmin, clinicId), patientId, tier3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertNothingRedeemed(patientId);
    }

    [Fact]
    public async Task Redeem_PatientOfAnotherClinic_Returns404_AndRedeemsNothing()
    {
        var (_, patientId, tier1, _) = await SeedAsync();
        var otherClinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 100m, 1_000);

        var redeem = await RedeemAsync(ClientFor(UserRole.ClinicAdmin, otherClinicId), patientId, tier1);
        var view = await ClientFor(UserRole.ClinicAdmin, otherClinicId).GetAsync($"/api/patients/{patientId}/rewards");

        Assert.Equal(HttpStatusCode.NotFound, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);
        await AssertNothingRedeemed(patientId);
    }

    [Fact]
    public async Task Redeem_RewardOfAnotherClinic_Returns404_AndRedeemsNothing()
    {
        var (clinicId, patientId, _, _) = await SeedAsync();
        var otherClinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic B", 100m, 1_000);
        var otherReward = await TestDataSeeder.SeedRewardAsync(_factory, otherClinicId, "Peskiranje", 0, 0);

        var response = await RedeemAsync(ClientFor(UserRole.ClinicAdmin, clinicId), patientId, otherReward);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingRedeemed(patientId);
    }

    [Fact]
    public async Task WithoutToken_Returns401_AndRedeemsNothing()
    {
        var (_, patientId, tier1, _) = await SeedAsync();

        var redeem = await RedeemAsync(_factory.CreateClient(), patientId, tier1);
        var view = await _factory.CreateClient().GetAsync($"/api/patients/{patientId}/rewards");

        Assert.Equal(HttpStatusCode.Unauthorized, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, view.StatusCode);
        await AssertNothingRedeemed(patientId);
    }

    // A patient cannot confirm their own redemption (SECURITY.md §5.4).
    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.PlatformAdmin)]
    public async Task AsOtherRole_Returns403_AndRedeemsNothing(UserRole role)
    {
        var (clinicId, patientId, tier1, _) = await SeedAsync();
        var client = ClientFor(role, role == UserRole.PlatformAdmin ? null : clinicId);

        var redeem = await RedeemAsync(client, patientId, tier1);
        var view = await client.GetAsync($"/api/patients/{patientId}/rewards");

        Assert.Equal(HttpStatusCode.Forbidden, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, view.StatusCode);
        await AssertNothingRedeemed(patientId);
    }

    // The right role but no tenant claim must not widen access - it fails closed.
    [Fact]
    public async Task ClinicAdminTokenWithoutTenantClaim_Returns404_AndRedeemsNothing()
    {
        var (_, patientId, tier1, _) = await SeedAsync();
        var client = ClientFor(UserRole.ClinicAdmin, clinicId: null);

        Assert.Equal(HttpStatusCode.NotFound, (await RedeemAsync(client, patientId, tier1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{patientId}/rewards")).StatusCode);
        await AssertNothingRedeemed(patientId);
    }

    private sealed record RedeemedResponse(Guid Id, Guid RewardId, bool CycleCompleted);

    private sealed record RewardsResponse(int Points, int ReferralBalance, List<RewardResponse> Rewards);

    private sealed record RewardResponse(Guid Id, string Name, string? Description, int RequiredPoints, int RequiredReferrals, string Status);
}
