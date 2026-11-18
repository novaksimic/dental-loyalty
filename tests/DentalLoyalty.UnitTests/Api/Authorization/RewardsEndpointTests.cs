using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// Reward definition management (/api/rewards) through the real pipeline:
// ClinicAdmin only; every read and write is of the caller's own clinic; the
// Reward's own validation answers 400 per field and saves nothing; rewards are
// deactivated and activated, never deleted, and no clinic starts with any.
//
// The patient-facing rules are not changed by this API, and the last section
// proves it through the existing endpoints: availability follows the current
// definition and the patient's balances, an inactive reward cannot be
// redeemed, and a redemption never deducts points or referrals.
public class RewardsEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public RewardsEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(UserRole? role = null, Guid? clinicId = null, Guid? userId = null)
    {
        var client = _factory.CreateClient();

        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", TestJwt.CreateValidToken(role.Value, clinicId, userId));
        }

        return client;
    }

    // A user id is included because redemption records the confirming admin.
    private HttpClient Admin(Guid clinicId) => Client(UserRole.ClinicAdmin, clinicId, Guid.NewGuid());

    private Task<Guid> SeedClinicAsync(string name = "Clinic A") =>
        TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, name, 100m, 1_000);

    private static object Body(
        string name = "Peskiranje",
        int requiredPoints = 300_000,
        int requiredReferrals = 1,
        string? description = "Profesionalno peskiranje") =>
        new { name, description, requiredPoints, requiredReferrals };

    private async Task<Guid> CreateAsync(Guid clinicId, object? body = null)
    {
        var response = await Admin(clinicId).PostAsJsonAsync("/api/rewards", body ?? Body());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Reward?> ReadRewardAsync(Guid rewardId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>()
            .Rewards.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rewardId);
    }

    private async Task<int> RewardCountAsync(Guid clinicId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>()
            .Rewards.CountAsync(r => r.ClinicId == clinicId);
    }

    // --- Authorization ---------------------------------------------------

    public static TheoryData<string, string> Endpoints() => new()
    {
        { "GET", "/api/rewards" },
        { "GET", $"/api/rewards/{Guid.NewGuid()}" },
        { "POST", "/api/rewards" },
        { "PUT", $"/api/rewards/{Guid.NewGuid()}" },
        { "POST", $"/api/rewards/{Guid.NewGuid()}/deactivation" },
        { "POST", $"/api/rewards/{Guid.NewGuid()}/activation" },
    };

    private static HttpRequestMessage Request(string method, string url) =>
        new(new HttpMethod(method), url)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(Body()) : null,
        };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task WithoutToken_Returns401(string method, string url)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().SendAsync(Request(method, url))).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AsPlatformAdmin_Returns403(string method, string url)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(UserRole.PlatformAdmin).SendAsync(Request(method, url))).StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AsPatient_Returns403(string method, string url)
    {
        var clinicId = await SeedClinicAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Client(UserRole.Patient, clinicId).SendAsync(Request(method, url))).StatusCode);
    }

    [Fact]
    public async Task ClinicAdminWithoutClinicClaim_CannotListOrCreate()
    {
        var client = Client(UserRole.ClinicAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/rewards")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/rewards", Body())).StatusCode);
    }

    // --- No seeding ------------------------------------------------------

    // A clinic created through the platform starts with no rewards at all.
    [Fact]
    public async Task NewClinic_HasNoRewards()
    {
        var created = await Client(UserRole.PlatformAdmin).PostAsJsonAsync("/api/clinics", new { name = "Brand new clinic" });
        var clinicId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var rewards = await Admin(clinicId).GetFromJsonAsync<JsonElement[]>("/api/rewards");

        Assert.Empty(rewards!);
        Assert.Equal(0, await RewardCountAsync(clinicId));
    }

    // --- Create ----------------------------------------------------------

    [Fact]
    public async Task Create_Returns201_WithLocation_AndTheSavedDefinition()
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PostAsJsonAsync(
            "/api/rewards",
            Body(name: "  Kompozitni ispun ", requiredPoints: 500_000, requiredReferrals: 1, description: "  Jedan ispun  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        Assert.Equal($"/api/rewards/{id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal("Kompozitni ispun", body.GetProperty("name").GetString());
        Assert.Equal("Jedan ispun", body.GetProperty("description").GetString());
        Assert.Equal(500_000, body.GetProperty("requiredPoints").GetInt32());
        Assert.Equal(1, body.GetProperty("requiredReferrals").GetInt32());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(6, body.EnumerateObject().Count());

        var saved = (await ReadRewardAsync(id))!;
        Assert.Equal(clinicId, saved.ClinicId);
        Assert.Equal("Kompozitni ispun", saved.Name);
    }

    [Fact]
    public async Task Create_WithoutDescription_OrBlank_StoresNone()
    {
        var clinicId = await SeedClinicAsync();

        var withoutId = await CreateAsync(clinicId, new { name = "Peskiranje", requiredPoints = 300_000, requiredReferrals = 1 });
        var blankId = await CreateAsync(clinicId, Body(description: "   "));

        Assert.Null((await ReadRewardAsync(withoutId))!.Description);
        Assert.Null((await ReadRewardAsync(blankId))!.Description);
    }

    // The boundaries the Reward accepts: 0 and 1,000,000 points (the maximum
    // balance), 0 referrals, and more referrals than a cycle can earn.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1_000_000, 5)]
    [InlineData(100_000, 6)]
    public async Task Create_AtValidBoundaries_Succeeds(int requiredPoints, int requiredReferrals)
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PostAsJsonAsync("/api/rewards", Body(requiredPoints: requiredPoints, requiredReferrals: requiredReferrals));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_IgnoresClinicIdInBody()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");

        var response = await Admin(clinicA).PostAsJsonAsync(
            "/api/rewards",
            new { name = "Peskiranje", requiredPoints = 300_000, requiredReferrals = 1, clinicId = clinicB });

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(clinicA, (await ReadRewardAsync(id))!.ClinicId);
        Assert.Equal(0, await RewardCountAsync(clinicB));
    }

    public static TheoryData<object, string> InvalidBodies() => new()
    {
        { new { name = "   ", requiredPoints = 1, requiredReferrals = 0 }, "Name" },
        { new { name = new string('n', Reward.MaximumNameLength + 1), requiredPoints = 1, requiredReferrals = 0 }, "Name" },
        { new { name = "Peskiranje", description = new string('d', Reward.MaximumDescriptionLength + 1), requiredPoints = 1, requiredReferrals = 0 }, "Description" },
        { new { name = "Peskiranje", requiredPoints = -1, requiredReferrals = 0 }, "RequiredPoints" },
        { new { name = "Peskiranje", requiredPoints = LoyaltyAccount.MaximumPointsBalance + 1, requiredReferrals = 0 }, "RequiredPoints" },
        { new { name = "Peskiranje", requiredPoints = 1, requiredReferrals = -1 }, "RequiredReferrals" },
        { new { name = "Peskiranje", requiredReferrals = 0 }, "RequiredPoints" },
        { new { name = "Peskiranje", requiredPoints = 1 }, "RequiredReferrals" },
        { new { requiredPoints = 1, requiredReferrals = 0 }, "Name" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_InvalidConfiguration_Returns400_WithFieldError_AndSavesNothing(object body, string field)
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PostAsJsonAsync("/api/rewards", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for {field}: {errors}");
        Assert.Equal(0, await RewardCountAsync(clinicId));
    }

    // --- Read ------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsOnlyOwnClinicsRewards_InMilestoneOrder_WithStatusFilter()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");

        var tier3 = await CreateAsync(clinicA, Body("Kucno izbeljivanje", 1_000_000, 5));
        var tier1 = await CreateAsync(clinicA, Body("Peskiranje", 300_000, 1));
        var tier2 = await CreateAsync(clinicA, Body("Popust na protezu", 600_000, 3));
        await CreateAsync(clinicB, Body("Reward of B", 1, 0));
        await Admin(clinicA).PostAsync($"/api/rewards/{tier2}/deactivation", null);

        var all = await Admin(clinicA).GetFromJsonAsync<JsonElement[]>("/api/rewards");
        var active = await Admin(clinicA).GetFromJsonAsync<JsonElement[]>("/api/rewards?isActive=true");
        var inactive = await Admin(clinicA).GetFromJsonAsync<JsonElement[]>("/api/rewards?isActive=false");

        Assert.Equal(new[] { tier1, tier2, tier3 }, all!.Select(r => r.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { tier1, tier3 }, active!.Select(r => r.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { tier2 }, inactive!.Select(r => r.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task GetById_ReturnsOwnReward()
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId, Body("Peskiranje", 300_000, 1, "Opis"));

        var body = await Admin(clinicId).GetFromJsonAsync<JsonElement>($"/api/rewards/{id}");

        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("Peskiranje", body.GetProperty("name").GetString());
        Assert.Equal("Opis", body.GetProperty("description").GetString());
        Assert.Equal(300_000, body.GetProperty("requiredPoints").GetInt32());
        Assert.Equal(1, body.GetProperty("requiredReferrals").GetInt32());
        Assert.True(body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task UnknownId_Returns404_ForEveryIdOperation()
    {
        var clinicId = await SeedClinicAsync();
        var admin = Admin(clinicId);
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/rewards/{unknown}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/rewards/{unknown}", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/rewards/{unknown}/deactivation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/rewards/{unknown}/activation", null)).StatusCode);
    }

    // --- Tenant isolation ------------------------------------------------

    [Fact]
    public async Task AnotherClinicsReward_IsNotFound_ForEveryOperation_AndUnchanged()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");
        var rewardOfB = await CreateAsync(clinicB, Body("Peskiranje", 300_000, 1, "Opis"));
        var adminA = Admin(clinicA);

        Assert.Equal(HttpStatusCode.NotFound, (await adminA.GetAsync($"/api/rewards/{rewardOfB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminA.PutAsJsonAsync($"/api/rewards/{rewardOfB}", Body("Hijacked", 0, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminA.PostAsync($"/api/rewards/{rewardOfB}/deactivation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminA.PostAsync($"/api/rewards/{rewardOfB}/activation", null)).StatusCode);

        var unchanged = (await ReadRewardAsync(rewardOfB))!;
        Assert.Equal("Peskiranje", unchanged.Name);
        Assert.Equal(300_000, unchanged.RequiredPoints);
        Assert.True(unchanged.IsActive);
        Assert.Null(unchanged.UpdatedAt);
    }

    // --- Update ----------------------------------------------------------

    [Fact]
    public async Task Update_Returns204_AndReplacesTheDefinition()
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId, Body("Peskiranje", 300_000, 1, "Opis"));

        var response = await Admin(clinicId).PutAsJsonAsync(
            $"/api/rewards/{id}",
            new { name = "Uklanjanje kamenca", requiredPoints = 400_000, requiredReferrals = 2 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var saved = (await ReadRewardAsync(id))!;
        Assert.Equal("Uklanjanje kamenca", saved.Name);
        Assert.Null(saved.Description);
        Assert.Equal(400_000, saved.RequiredPoints);
        Assert.Equal(2, saved.RequiredReferrals);
        Assert.True(saved.IsActive);
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Update_InvalidConfiguration_Returns400_AndChangesNothing(object body, string field)
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId, Body("Peskiranje", 300_000, 1, "Opis"));

        var response = await Admin(clinicId).PutAsJsonAsync($"/api/rewards/{id}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));

        var unchanged = (await ReadRewardAsync(id))!;
        Assert.Equal("Peskiranje", unchanged.Name);
        Assert.Equal("Opis", unchanged.Description);
        Assert.Equal(300_000, unchanged.RequiredPoints);
        Assert.Equal(1, unchanged.RequiredReferrals);
    }

    // --- Activation ------------------------------------------------------

    [Fact]
    public async Task Deactivate_ThenActivate_TogglesStatus_AndIsIdempotent()
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId);
        var admin = Admin(clinicId);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/rewards/{id}/deactivation", null)).StatusCode);
        var deactivated = (await ReadRewardAsync(id))!;
        Assert.False(deactivated.IsActive);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/rewards/{id}/deactivation", null)).StatusCode);
        Assert.Equal(deactivated.UpdatedAt, (await ReadRewardAsync(id))!.UpdatedAt);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/rewards/{id}/activation", null)).StatusCode);
        Assert.True((await ReadRewardAsync(id))!.IsActive);
    }

    // --- Existing availability and redemption rules, unchanged --------------

    private async Task<Guid> SeedLinkedPatientAsync(Guid clinicId, int points, int referrals)
    {
        var (_, patientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Ana");
        await TestDataSeeder.SetLoyaltyBalancesAsync(_factory, patientId, points, referrals);
        return patientId;
    }

    private async Task<Dictionary<Guid, string>> PatientRewardStatusesAsync(Guid clinicId, Guid patientId)
    {
        var body = await Admin(clinicId).GetFromJsonAsync<JsonElement>($"/api/patients/{patientId}/rewards");

        return body.GetProperty("rewards").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetGuid(), r => r.GetProperty("status").GetString()!);
    }

    // A created reward is evaluated for the patient straight away: Available
    // only when both requirements are met (BUSINESS.md §10).
    [Fact]
    public async Task CreatedReward_IsAvailable_OnlyWhenBothRequirementsAreMet()
    {
        var clinicId = await SeedClinicAsync();
        var patientId = await SeedLinkedPatientAsync(clinicId, points: 600_000, referrals: 1);

        var unlocked = await CreateAsync(clinicId, Body("Peskiranje", 300_000, 1));
        var needsReferrals = await CreateAsync(clinicId, Body("Popust", 600_000, 3));
        var needsPoints = await CreateAsync(clinicId, Body("Izbeljivanje", 1_000_000, 1));

        var statuses = await PatientRewardStatusesAsync(clinicId, patientId);

        Assert.Equal("Available", statuses[unlocked]);
        Assert.Equal("Locked", statuses[needsReferrals]);
        Assert.Equal("Locked", statuses[needsPoints]);
    }

    // Requirements are read when availability is evaluated: changing them
    // changes availability from now on.
    [Fact]
    public async Task UpdatedRequirements_ApplyToAvailability()
    {
        var clinicId = await SeedClinicAsync();
        var patientId = await SeedLinkedPatientAsync(clinicId, points: 300_000, referrals: 1);
        var id = await CreateAsync(clinicId, Body("Peskiranje", 300_000, 1));

        Assert.Equal("Available", (await PatientRewardStatusesAsync(clinicId, patientId))[id]);

        await Admin(clinicId).PutAsJsonAsync($"/api/rewards/{id}", Body("Peskiranje", 400_000, 1));

        Assert.Equal("Locked", (await PatientRewardStatusesAsync(clinicId, patientId))[id]);
    }

    // An inactive reward is not offered to the patient and cannot be redeemed;
    // activating it offers it again.
    [Fact]
    public async Task InactiveReward_CannotBeRedeemed_UntilActivatedAgain()
    {
        var clinicId = await SeedClinicAsync();
        var patientId = await SeedLinkedPatientAsync(clinicId, points: 300_000, referrals: 1);
        var id = await CreateAsync(clinicId, Body("Peskiranje", 300_000, 1));
        var admin = Admin(clinicId);

        await admin.PostAsync($"/api/rewards/{id}/deactivation", null);

        Assert.DoesNotContain(id, (await PatientRewardStatusesAsync(clinicId, patientId)).Keys);

        var rejected = await admin.PostAsync($"/api/patients/{patientId}/rewards/{id}/redemptions", null);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Empty(await TestDataSeeder.GetRewardRedemptionsAsync(_factory, patientId));

        await admin.PostAsync($"/api/rewards/{id}/activation", null);

        Assert.Equal("Available", (await PatientRewardStatusesAsync(clinicId, patientId))[id]);

        var redeemed = await admin.PostAsync($"/api/patients/{patientId}/rewards/{id}/redemptions", null);
        Assert.Equal(HttpStatusCode.Created, redeemed.StatusCode);
    }

    // BUSINESS.md §13: redeeming consumes neither points nor referral credits,
    // and a reward redeemed in the cycle stays shown as Redeemed even after the
    // clinic withdraws it.
    [Fact]
    public async Task Redemption_DeductsNothing_AndStaysRedeemed_AfterDeactivation()
    {
        var clinicId = await SeedClinicAsync();
        var patientId = await SeedLinkedPatientAsync(clinicId, points: 450_000, referrals: 2);
        var id = await CreateAsync(clinicId, Body("Peskiranje", 300_000, 1));
        var admin = Admin(clinicId);

        var redeemed = await admin.PostAsync($"/api/patients/{patientId}/rewards/{id}/redemptions", null);
        Assert.Equal(HttpStatusCode.Created, redeemed.StatusCode);

        var account = Assert.Single(await TestDataSeeder.GetLoyaltyAccountsAsync(_factory, patientId));
        Assert.Equal(450_000, account.Points);
        Assert.Equal(2, account.ReferralBalance);

        await admin.PostAsync($"/api/rewards/{id}/deactivation", null);

        Assert.Equal("Redeemed", (await PatientRewardStatusesAsync(clinicId, patientId))[id]);
    }
}
