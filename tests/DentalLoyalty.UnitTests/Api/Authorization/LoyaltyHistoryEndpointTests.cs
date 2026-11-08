using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.UnitTests.Api.TestSupport;
using DentalLoyalty.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// GET /api/patients/me/loyalty-history[/transactions] (Patient, own history only)
// and GET /api/patients/{id}/loyalty-history[/transactions] (ClinicAdmin, own
// clinic only) through the real pipeline. The history is produced by the real
// write endpoints - recording services, validating referrals, redeeming rewards -
// so the read is checked against what the operations actually record.
public class LoyaltyHistoryEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public LoyaltyHistoryEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private sealed record Clinic(Guid ClinicId, Guid ServiceId);

    private sealed record LinkedPatient(Guid UserId, Guid PatientId);

    private async Task<Clinic> SeedClinicAsync(string name = "Clinic A")
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, name, 100m, 1_000);
        var serviceId = await TestDataSeeder.SeedServiceAsync(_factory, clinicId, 7_500m);
        return new Clinic(clinicId, serviceId);
    }

    private async Task<LinkedPatient> SeedLinkedPatientAsync(Guid clinicId, string name)
    {
        var (userId, patientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, name);
        return new LinkedPatient(userId, patientId);
    }

    private HttpClient ClientFor(UserRole role, Guid? clinicId, Guid? userId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwt.CreateValidToken(role, clinicId, userId));
        return client;
    }

    private HttpClient PatientClient(Guid clinicId, LinkedPatient patient) =>
        ClientFor(UserRole.Patient, clinicId, patient.UserId);

    private HttpClient AdminClient(Guid clinicId) => ClientFor(UserRole.ClinicAdmin, clinicId);

    private static async Task<T> ReadOkAsync<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HistoryResponse> MyHistoryAsync(HttpClient client) =>
        await ReadOkAsync<HistoryResponse>(await client.GetAsync("/api/patients/me/loyalty-history"));

    private static async Task<TransactionsResponse> MyTransactionsAsync(HttpClient client, string query = "") =>
        await ReadOkAsync<TransactionsResponse>(await client.GetAsync($"/api/patients/me/loyalty-history/transactions{query}"));

    private static async Task RecordServiceAsync(HttpClient admin, Guid patientId, Guid serviceId)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/patients/{patientId}/services",
            new { id = Guid.NewGuid(), serviceId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<Guid> CreateReferralAsync(HttpClient admin, Guid referrerId, Guid referredId)
    {
        var response = await admin.PostAsJsonAsync(
            "/api/referrals",
            new { referrerPatientId = referrerId, referredPatientId = referredId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static async Task ValidateReferralAsync(HttpClient admin, Guid referralId)
    {
        var response = await admin.PostAsync($"/api/referrals/{referralId}/validation", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task RedeemAsync(HttpClient admin, Guid patientId, Guid rewardId)
    {
        var response = await admin.PostAsync($"/api/patients/{patientId}/rewards/{rewardId}/redemptions", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task BackdateCycleAsync(Guid patientId, DateTime startedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
        (await db.LoyaltyCycles.SingleAsync(c => c.PatientId == patientId && c.Status == LoyaltyCycleStatus.Active))
            .StartedOn(startedAt);
        await db.SaveChangesAsync();
    }

    // Everything a history read could change for the patient.
    private async Task<string> SnapshotAsync(Guid patientId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
        var account = await db.LoyaltyAccounts.AsNoTracking().SingleAsync(a => a.PatientId == patientId);
        var cycles = await db.LoyaltyCycles.AsNoTracking().Where(c => c.PatientId == patientId)
            .OrderBy(c => c.StartedAt).Select(c => $"{c.Id}:{c.Status}:{c.StartedAt:O}:{c.EndedAt:O}").ToListAsync();
        var transactions = await db.LoyaltyTransactions.AsNoTracking().Where(t => t.PatientId == patientId)
            .OrderBy(t => t.Id).Select(t => $"{t.Id}:{t.Type}:{t.PointsChange}:{t.ReferralChange}").ToListAsync();
        var redemptions = await db.RewardRedemptions.AsNoTracking().CountAsync(r => r.PatientId == patientId);
        var referrals = await db.Referrals.AsNoTracking().Where(r => r.ReferrerPatientId == patientId || r.ReferredPatientId == patientId)
            .OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.Status}:{r.RewardsForfeited}").ToListAsync();

        return string.Join("|",
            $"{account.Points}/{account.ReferralBalance}/{account.UpdatedAt:O}",
            string.Join(",", cycles),
            string.Join(",", transactions),
            redemptions,
            string.Join(",", referrals));
    }

    // BUSINESS.md §14, §17: a service, a referral and a redemption, all seen by
    // the patient - balances, reward states, the cycle with its redemption, the
    // referral and every transaction, newest first.
    [Fact]
    public async Task Patient_GetsOwnHistory_ReflectingTheRecordedOperations()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var marko = await SeedLinkedPatientAsync(clinic.ClinicId, "Marko");
        var peskiranje = await TestDataSeeder.SeedRewardAsync(_factory, clinic.ClinicId, "Peskiranje", 100_000, 1);
        await TestDataSeeder.SeedRewardAsync(_factory, clinic.ClinicId, "Kompozitni ispun", 500_000, 1);
        var admin = AdminClient(clinic.ClinicId);

        await RecordServiceAsync(admin, ana.PatientId, clinic.ServiceId);
        await ValidateReferralAsync(admin, await CreateReferralAsync(admin, ana.PatientId, marko.PatientId));
        await RedeemAsync(admin, ana.PatientId, peskiranje);

        var client = PatientClient(clinic.ClinicId, ana);
        var history = await MyHistoryAsync(client);
        var transactions = await MyTransactionsAsync(client);

        // Redemption deducts nothing (§13): 75,000 + 50,000 points, 1 referral.
        Assert.Equal((125_000, 1_000_000, 1, 5),
            (history.Status.Points, history.Status.MaximumPoints, history.Status.ReferralBalance, history.Status.MaximumReferralRewards));
        Assert.NotNull(history.Status.ActiveCycle);
        Assert.Equal(history.Status.ActiveCycle!.StartedAt.AddYears(1), history.Status.ActiveCycle.ExpiresAt);
        Assert.Equal(
            new[] { ("Peskiranje", "Redeemed"), ("Kompozitni ispun", "Locked") },
            history.Status.Rewards.Select(r => (r.Name, r.Status)));

        var cycle = Assert.Single(history.Cycles);
        Assert.Equal(("Active", 125_000, 1), (cycle.Status, cycle.Points!.Value, cycle.Referrals!.Value));
        Assert.Null(cycle.EndedAt);
        Assert.Equal("Peskiranje", Assert.Single(cycle.Redemptions).RewardName);

        var referral = Assert.Single(history.Referrals);
        Assert.Equal(("Completed", false, true, 50_000), (referral.Status, referral.RewardsForfeited, referral.Rewarded, referral.CreditedPoints!.Value));
        Assert.NotNull(referral.ExaminedAt);

        Assert.Equal(3, transactions.TotalCount);
        Assert.Equal(
            new (string, int, int, string?, string?, string?)[]
            {
                ("RewardRedeemed", 0, 0, null, "Peskiranje", null),
                ("ReferralPointsEarned", 50_000, 1, null, null, "Referrer"),
                ("ServicePointsEarned", 75_000, 0, "Usluga 7500", null, null),
            },
            transactions.Items.Select(t => (t.Type, t.PointsChange, t.ReferralChange, t.ServiceName, t.RewardName, t.ReferralRole)));
        Assert.True(transactions.Items.Zip(transactions.Items.Skip(1)).All(p => p.First.OccurredAt >= p.Second.OccurredAt));

        // The referred patient sees the same referral from their side.
        var markoTransaction = Assert.Single((await MyTransactionsAsync(PatientClient(clinic.ClinicId, marko))).Items);
        Assert.Equal(("ReferralPointsEarned", 50_000, 1, "Referred"),
            (markoTransaction.Type, markoTransaction.PointsChange, markoTransaction.ReferralChange, markoTransaction.ReferralRole));
        Assert.Empty((await MyHistoryAsync(PatientClient(clinic.ClinicId, marko))).Referrals);
    }

    // /me is resolved from the token alone: two patients of one clinic each see
    // only their own history, and a patient cannot use the id-based endpoints.
    [Fact]
    public async Task Patient_SeesOnlyOwnHistory_AndCannotRequestAnotherPatientById()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var marko = await SeedLinkedPatientAsync(clinic.ClinicId, "Marko");
        await RecordServiceAsync(AdminClient(clinic.ClinicId), marko.PatientId, clinic.ServiceId);
        var anaClient = PatientClient(clinic.ClinicId, ana);

        Assert.Equal(0, (await MyHistoryAsync(anaClient)).Status.Points);
        Assert.Equal(0, (await MyTransactionsAsync(anaClient)).TotalCount);
        Assert.Equal(75_000, (await MyHistoryAsync(PatientClient(clinic.ClinicId, marko))).Status.Points);

        Assert.Equal(HttpStatusCode.Forbidden, (await anaClient.GetAsync($"/api/patients/{marko.PatientId}/loyalty-history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anaClient.GetAsync($"/api/patients/{marko.PatientId}/loyalty-history/transactions")).StatusCode);
    }

    // SECURITY.md §6.6: a patient token whose tenant claim is another clinic
    // resolves to no patient, and a user with no linked patient gets nothing.
    [Fact]
    public async Task Patient_WithAnotherClinicsTenantClaim_OrNoLinkedPatient_Gets404()
    {
        var clinic = await SeedClinicAsync();
        var other = await SeedClinicAsync("Clinic B");
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");

        foreach (var client in new[]
        {
            ClientFor(UserRole.Patient, other.ClinicId, ana.UserId),
            ClientFor(UserRole.Patient, clinic.ClinicId, Guid.NewGuid()),
            ClientFor(UserRole.Patient, clinicId: null, ana.UserId),
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/patients/me/loyalty-history")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/patients/me/loyalty-history/transactions")).StatusCode);
        }
    }

    [Fact]
    public async Task ClinicAdmin_GetsHistoryOfOwnClinicsPatient()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var admin = AdminClient(clinic.ClinicId);
        await RecordServiceAsync(admin, ana.PatientId, clinic.ServiceId);

        var history = await ReadOkAsync<HistoryResponse>(await admin.GetAsync($"/api/patients/{ana.PatientId}/loyalty-history"));
        var transactions = await ReadOkAsync<TransactionsResponse>(await admin.GetAsync($"/api/patients/{ana.PatientId}/loyalty-history/transactions"));

        Assert.Equal(75_000, history.Status.Points);
        Assert.Equal("ServicePointsEarned", Assert.Single(transactions.Items).Type);
    }

    // BUSINESS.md §21: another clinic's patient is indistinguishable from none.
    [Fact]
    public async Task ClinicAdmin_OfAnotherClinic_OrWithoutTenant_Gets404()
    {
        var clinic = await SeedClinicAsync();
        var other = await SeedClinicAsync("Clinic B");
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");

        foreach (var client in new[] { AdminClient(other.ClinicId), ClientFor(UserRole.ClinicAdmin, clinicId: null) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{ana.PatientId}/loyalty-history")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{ana.PatientId}/loyalty-history/transactions")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await AdminClient(clinic.ClinicId).GetAsync($"/api/patients/{Guid.NewGuid()}/loyalty-history")).StatusCode);
    }

    // SECURITY.md §5.4, §7.12: PlatformAdmin is not granted patient data; a
    // ClinicAdmin has no "own" loyalty history.
    [Fact]
    public async Task OtherRoles_Get403_AndAnonymousGets401()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var platformAdmin = ClientFor(UserRole.PlatformAdmin, clinicId: null);
        var clinicAdmin = AdminClient(clinic.ClinicId);
        var anonymous = _factory.CreateClient();

        foreach (var url in new[] { "/api/patients/me/loyalty-history", "/api/patients/me/loyalty-history/transactions" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await platformAdmin.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clinicAdmin.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        }

        foreach (var url in new[] { $"/api/patients/{ana.PatientId}/loyalty-history", $"/api/patients/{ana.PatientId}/loyalty-history/transactions" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await platformAdmin.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task NewlyRegisteredPatient_GetsAnEmptyHistory()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        await TestDataSeeder.SeedRewardAsync(_factory, clinic.ClinicId, "Peskiranje", 300_000, 1);
        var client = PatientClient(clinic.ClinicId, ana);

        var history = await MyHistoryAsync(client);
        var transactions = await MyTransactionsAsync(client);

        Assert.Equal((0, 0), (history.Status.Points, history.Status.ReferralBalance));
        Assert.NotNull(history.Status.ActiveCycle);
        Assert.Equal("Locked", Assert.Single(history.Status.Rewards).Status);
        var cycle = Assert.Single(history.Cycles);
        Assert.Equal(("Active", 0, 0), (cycle.Status, cycle.Points!.Value, cycle.Referrals!.Value));
        Assert.Empty(cycle.Redemptions);
        Assert.Empty(history.Referrals);
        Assert.Equal((1, 20, 0), (transactions.Page, transactions.PageSize, transactions.TotalCount));
        Assert.Empty(transactions.Items);
    }

    // BUSINESS.md §5.1: before registration and linking there is no cycle and no
    // reward is available.
    [Fact]
    public async Task UnregisteredPatient_HasNoCycle_AndEveryRewardLocked()
    {
        var clinic = await SeedClinicAsync();
        var patientId = await TestDataSeeder.SeedUnlinkedPatientAsync(_factory, clinic.ClinicId);
        await TestDataSeeder.SeedRewardAsync(_factory, clinic.ClinicId, "Free", 0, 0);

        var history = await ReadOkAsync<HistoryResponse>(await AdminClient(clinic.ClinicId).GetAsync($"/api/patients/{patientId}/loyalty-history"));

        Assert.Null(history.Status.ActiveCycle);
        Assert.Empty(history.Cycles);
        Assert.Equal("Locked", Assert.Single(history.Status.Rewards).Status);
    }

    // BUSINESS.md §4.2: a referral to a patient who was not registered and linked
    // is kept in history, forfeited, with nothing credited; a pending one shows
    // no examination yet.
    [Fact]
    public async Task ReferralHistory_ShowsForfeitedAndPendingReferrals()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var marko = await SeedLinkedPatientAsync(clinic.ClinicId, "Marko");
        var unlinked = await TestDataSeeder.SeedUnlinkedPatientAsync(_factory, clinic.ClinicId);
        var admin = AdminClient(clinic.ClinicId);

        await ValidateReferralAsync(admin, await CreateReferralAsync(admin, ana.PatientId, unlinked));
        await CreateReferralAsync(admin, ana.PatientId, marko.PatientId);

        var client = PatientClient(clinic.ClinicId, ana);
        var history = await MyHistoryAsync(client);

        Assert.Equal(
            new (string, bool, bool, int?, bool)[]
            {
                ("Pending", false, false, null, false),
                ("Completed", true, false, null, true),
            },
            history.Referrals.Select(r => (r.Status, r.RewardsForfeited, r.Rewarded, r.CreditedPoints, r.ExaminedAt is not null)));
        Assert.Equal((0, 0), (history.Status.Points, history.Status.ReferralBalance));
        Assert.Equal(0, (await MyTransactionsAsync(client)).TotalCount);
    }

    // BUSINESS.md §5.2, §5.4: the redemption that completes the cycle and the
    // reset it causes share one time; the reset is the later record. The
    // completed cycle keeps its final progress and its redemption.
    [Fact]
    public async Task CompletedCycle_IsShownWithItsFinalProgress_AndTheResetIsTheNewestTransaction()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        await TestDataSeeder.SetLoyaltyBalancesAsync(_factory, ana.PatientId, 1_000_000, 5);
        var tier3 = await TestDataSeeder.SeedRewardAsync(_factory, clinic.ClinicId, "Kućno izbeljivanje zuba", 1_000_000, 5);
        await RedeemAsync(AdminClient(clinic.ClinicId), ana.PatientId, tier3);
        var client = PatientClient(clinic.ClinicId, ana);

        var history = await MyHistoryAsync(client);
        var transactions = await MyTransactionsAsync(client);

        Assert.Equal((0, 0), (history.Status.Points, history.Status.ReferralBalance));
        Assert.Equal("Locked", Assert.Single(history.Status.Rewards).Status);
        Assert.Equal(new[] { "Active", "Completed" }, history.Cycles.Select(c => c.Status));
        var completed = history.Cycles[1];
        Assert.Equal((1_000_000, 5), (completed.Points!.Value, completed.Referrals!.Value));
        Assert.Equal(completed.EndedAt, history.Cycles[0].StartedAt);
        Assert.Null(completed.ExpiresAt);
        Assert.Equal("Kućno izbeljivanje zuba", Assert.Single(completed.Redemptions).RewardName);
        Assert.Empty(history.Cycles[0].Redemptions);

        Assert.Equal(
            new (string, int, int, string?)[]
            {
                ("CycleReset", -1_000_000, -5, "Completed"),
                ("RewardRedeemed", 0, 0, null),
            },
            transactions.Items.Select(t => (t.Type, t.PointsChange, t.ReferralChange, t.EndedCycleStatus)));
    }

    // ADR-008 decision 6: an expired cycle is shown as the next operation will
    // record it - Expired, with its reset, and the next cycle active from its
    // end - while nothing at all is written.
    [Fact]
    public async Task ExpiredCycle_IsShownRolledOver_WithoutModifyingAnyRecord()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var admin = AdminClient(clinic.ClinicId);
        await RecordServiceAsync(admin, ana.PatientId, clinic.ServiceId);
        await RecordServiceAsync(admin, ana.PatientId, clinic.ServiceId);
        var start = DateTime.UtcNow.AddYears(-1).AddHours(-1);
        await BackdateCycleAsync(ana.PatientId, start);
        var before = await SnapshotAsync(ana.PatientId);
        var client = PatientClient(clinic.ClinicId, ana);

        var history = await MyHistoryAsync(client);
        var page1 = await MyTransactionsAsync(client, "?page=1&pageSize=2");
        var page2 = await MyTransactionsAsync(client, "?page=2&pageSize=2");
        await admin.GetAsync($"/api/patients/{ana.PatientId}/loyalty-history");

        Assert.Equal(before, await SnapshotAsync(ana.PatientId));

        Assert.Equal((0, 0), (history.Status.Points, history.Status.ReferralBalance));
        Assert.Equal(start.AddYears(1), history.Status.ActiveCycle!.StartedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(new[] { "Active", "Expired" }, history.Cycles.Select(c => c.Status));
        Assert.Equal((150_000, 0), (history.Cycles[1].Points!.Value, history.Cycles[1].Referrals!.Value));
        Assert.Equal(history.Cycles[1].EndedAt, history.Cycles[0].StartedAt);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(
            new[] { ("CycleReset", -150_000, "Expired"), ("ServicePointsEarned", 75_000, (string?)null) },
            page1.Items.Select(t => (t.Type, t.PointsChange, t.EndedCycleStatus)));
        Assert.Equal("ServicePointsEarned", Assert.Single(page2.Items).Type);
    }

    [Fact]
    public async Task Transactions_ArePaged_NewestFirst_WithoutGapsOrRepeats()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var admin = AdminClient(clinic.ClinicId);

        for (var i = 0; i < 5; i++)
        {
            await RecordServiceAsync(admin, ana.PatientId, clinic.ServiceId);
        }

        var client = PatientClient(clinic.ClinicId, ana);
        var all = (await MyTransactionsAsync(client, "?pageSize=100")).Items;
        var pages = new List<TransactionResponse>();

        for (var page = 1; page <= 3; page++)
        {
            var response = await MyTransactionsAsync(client, $"?page={page}&pageSize=2");
            Assert.Equal((page, 2, 5), (response.Page, response.PageSize, response.TotalCount));
            pages.AddRange(response.Items);
        }

        Assert.Equal(5, all.Count);
        Assert.Equal(all, pages);
        Assert.Equal(all.OrderByDescending(t => t.OccurredAt), all);
        Assert.Empty((await MyTransactionsAsync(client, "?page=4&pageSize=2")).Items);
        Assert.Empty((await MyTransactionsAsync(client, $"?page={int.MaxValue}&pageSize=100")).Items);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?page=-1&pageSize=10")]
    public async Task Transactions_WithInvalidPaging_Return400(string query)
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");

        var response = await PatientClient(clinic.ClinicId, ana).GetAsync($"/api/patients/me/loyalty-history/transactions{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Responses refer to records by name, time and status, never by internal id.
    [Fact]
    public async Task Responses_ExposeNoInternalIds()
    {
        var clinic = await SeedClinicAsync();
        var ana = await SeedLinkedPatientAsync(clinic.ClinicId, "Ana");
        var marko = await SeedLinkedPatientAsync(clinic.ClinicId, "Marko");
        var reward = await TestDataSeeder.SeedRewardAsync(_factory, clinic.ClinicId, "Peskiranje", 0, 0);
        var admin = AdminClient(clinic.ClinicId);
        await RecordServiceAsync(admin, ana.PatientId, clinic.ServiceId);
        await ValidateReferralAsync(admin, await CreateReferralAsync(admin, ana.PatientId, marko.PatientId));
        await RedeemAsync(admin, ana.PatientId, reward);
        var client = PatientClient(clinic.ClinicId, ana);

        var bodies = await (await client.GetAsync("/api/patients/me/loyalty-history")).Content.ReadAsStringAsync()
            + await (await client.GetAsync("/api/patients/me/loyalty-history/transactions")).Content.ReadAsStringAsync();

        foreach (var id in new[] { ana.PatientId, ana.UserId, marko.PatientId, clinic.ClinicId, clinic.ServiceId, reward })
        {
            Assert.DoesNotContain(id.ToString(), bodies, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("\"id\"", bodies, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record IdResponse(Guid Id);

    private sealed record HistoryResponse(StatusResponse Status, List<CycleResponse> Cycles, List<ReferralResponse> Referrals);

    private sealed record StatusResponse(
        int Points,
        int MaximumPoints,
        int ReferralBalance,
        int MaximumReferralRewards,
        ActiveCycleResponse? ActiveCycle,
        List<RewardResponse> Rewards);

    private sealed record ActiveCycleResponse(DateTime StartedAt, DateTime ExpiresAt);

    private sealed record RewardResponse(string Name, string? Description, int RequiredPoints, int RequiredReferrals, string Status);

    private sealed record CycleResponse(
        string Status,
        DateTime StartedAt,
        DateTime? EndedAt,
        DateTime? ExpiresAt,
        int? Points,
        int? Referrals,
        List<RedemptionResponse> Redemptions);

    private sealed record RedemptionResponse(string? RewardName, DateTime RedeemedAt);

    private sealed record ReferralResponse(
        string Status,
        DateTime ReferredAt,
        DateTime? ExaminedAt,
        bool RewardsForfeited,
        bool Rewarded,
        int? CreditedPoints);

    private sealed record TransactionsResponse(int Page, int PageSize, int TotalCount, List<TransactionResponse> Items);

    private sealed record TransactionResponse(
        DateTime OccurredAt,
        string Type,
        int PointsChange,
        int ReferralChange,
        string? ServiceName,
        string? RewardName,
        string? ReferralRole,
        string? EndedCycleStatus);
}
