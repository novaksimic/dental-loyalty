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

// The service catalog API (/api/services) through the real pipeline: ClinicAdmin
// only; every read and write is of the caller's own clinic (never a clinic from
// the request); validation answers 400 per field and saves nothing; services
// are deactivated and activated, never deleted; and an inactive service can no
// longer be recorded for a patient.
public class ServicesEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public ServicesEndpointTests(DentalLoyaltyApiFactory factory)
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

    private HttpClient Admin(Guid clinicId) => Client(UserRole.ClinicAdmin, clinicId);

    private Task<Guid> SeedClinicAsync(string name = "Clinic A") =>
        TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, name, 100m, 1_000);

    private async Task<Service?> ReadServiceAsync(Guid serviceId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>()
            .Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == serviceId);
    }

    private async Task<int> ServiceCountAsync(Guid clinicId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>()
            .Services.CountAsync(s => s.ClinicId == clinicId);
    }

    private async Task<Guid> CreateAsync(Guid clinicId, string name = "Pregled", decimal price = 2_000m)
    {
        var response = await Admin(clinicId).PostAsJsonAsync("/api/services", new { name, price });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // --- Authorization ---------------------------------------------------

    public static TheoryData<string, string> Endpoints() => new()
    {
        { "GET", "/api/services" },
        { "GET", $"/api/services/{Guid.NewGuid()}" },
        { "POST", "/api/services" },
        { "PUT", $"/api/services/{Guid.NewGuid()}" },
        { "POST", $"/api/services/{Guid.NewGuid()}/deactivation" },
        { "POST", $"/api/services/{Guid.NewGuid()}/activation" },
    };

    private static HttpRequestMessage Request(string method, string url) =>
        new(new HttpMethod(method), url)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(new { name = "Pregled", price = 100m }) : null,
        };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task WithoutToken_Returns401(string method, string url)
    {
        var response = await Client().SendAsync(Request(method, url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AsPlatformAdmin_Returns403(string method, string url)
    {
        var response = await Client(UserRole.PlatformAdmin).SendAsync(Request(method, url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AsPatient_Returns403(string method, string url)
    {
        var clinicId = await SeedClinicAsync();

        var response = await Client(UserRole.Patient, clinicId).SendAsync(Request(method, url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // A ClinicAdmin token without a clinic claim fails closed and creates nothing.
    [Fact]
    public async Task ClinicAdminWithoutClinicClaim_CannotListOrCreate()
    {
        var client = Client(UserRole.ClinicAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/services")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/services", new { name = "Pregled", price = 100m })).StatusCode);
    }

    // --- Create ----------------------------------------------------------

    [Fact]
    public async Task Create_Returns201_WithLocation_AndTheSavedService()
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PostAsJsonAsync("/api/services", new { name = "  Kompozitni ispun ", price = 7_500.50m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        Assert.Equal($"/api/services/{id}", response.Headers.Location!.AbsolutePath, ignoreCase: true);
        Assert.Equal("Kompozitni ispun", body.GetProperty("name").GetString());
        Assert.Equal(7_500.50m, body.GetProperty("price").GetDecimal());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(4, body.EnumerateObject().Count());

        var saved = (await ReadServiceAsync(id))!;
        Assert.Equal(clinicId, saved.ClinicId);
        Assert.Equal("Kompozitni ispun", saved.Name);
        Assert.Equal(7_500.50m, saved.Price);
    }

    [Fact]
    public async Task Create_WithZeroPrice_IsAllowed()
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PostAsJsonAsync("/api/services", new { name = "Kontrola", price = 0m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The clinic comes from the token: a clinic id smuggled into the body is
    // ignored, and nothing is created in that other clinic.
    [Fact]
    public async Task Create_IgnoresClinicIdInBody()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");

        var response = await Admin(clinicA).PostAsJsonAsync("/api/services", new { name = "Pregled", price = 100m, clinicId = clinicB });

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(clinicA, (await ReadServiceAsync(id))!.ClinicId);
        Assert.Equal(0, await ServiceCountAsync(clinicB));
    }

    public static TheoryData<object, string> InvalidBodies() => new()
    {
        { new { name = "   ", price = 100m }, "Name" },
        { new { name = new string('a', Service.MaximumNameLength + 1), price = 100m }, "Name" },
        { new { name = "Pregled", price = -1m }, "Price" },
        { new { name = "Pregled", price = 10.005m }, "Price" },
        { new { name = "Pregled" }, "Price" },
        { new { price = 100m }, "Name" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_Invalid_Returns400_WithFieldError_AndSavesNothing(object body, string field)
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PostAsJsonAsync("/api/services", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out _), $"Expected an error for {field}: {errors}");
        Assert.Equal(0, await ServiceCountAsync(clinicId));
    }

    // --- Read ------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsOnlyOwnClinicsServices_OrderedByName_WithStatusFilter()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");

        var zub = await CreateAsync(clinicA, "Zubni implant", 50_000m);
        var pregled = await CreateAsync(clinicA, "Pregled", 2_000m);
        var kontrola = await CreateAsync(clinicA, "Kontrola", 0m);
        await CreateAsync(clinicB, "Pregled B", 1_000m);
        await Admin(clinicA).PostAsync($"/api/services/{kontrola}/deactivation", null);

        var all = await Admin(clinicA).GetFromJsonAsync<JsonElement[]>("/api/services");
        var active = await Admin(clinicA).GetFromJsonAsync<JsonElement[]>("/api/services?isActive=true");
        var inactive = await Admin(clinicA).GetFromJsonAsync<JsonElement[]>("/api/services?isActive=false");

        Assert.Equal(new[] { kontrola, pregled, zub }, all!.Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { pregled, zub }, active!.Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { kontrola }, inactive!.Select(s => s.GetProperty("id").GetGuid()));
        Assert.False(inactive![0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetById_ReturnsOwnService()
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId, "Pregled", 2_000m);

        var body = await Admin(clinicId).GetFromJsonAsync<JsonElement>($"/api/services/{id}");

        Assert.Equal(id, body.GetProperty("id").GetGuid());
        Assert.Equal("Pregled", body.GetProperty("name").GetString());
        Assert.Equal(2_000m, body.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var clinicId = await SeedClinicAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Admin(clinicId).GetAsync($"/api/services/{Guid.NewGuid()}")).StatusCode);
    }

    // --- Tenant isolation ------------------------------------------------

    // Every id-based operation on another clinic's service is answered as for
    // no service, and changes nothing.
    [Fact]
    public async Task AnotherClinicsService_IsNotFound_ForEveryOperation_AndUnchanged()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");
        var serviceOfB = await CreateAsync(clinicB, "Pregled", 2_000m);
        var adminA = Admin(clinicA);

        Assert.Equal(HttpStatusCode.NotFound, (await adminA.GetAsync($"/api/services/{serviceOfB}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await adminA.PutAsJsonAsync($"/api/services/{serviceOfB}", new { name = "Hijacked", price = 1m })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminA.PostAsync($"/api/services/{serviceOfB}/deactivation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminA.PostAsync($"/api/services/{serviceOfB}/activation", null)).StatusCode);

        var unchanged = (await ReadServiceAsync(serviceOfB))!;
        Assert.Equal("Pregled", unchanged.Name);
        Assert.Equal(2_000m, unchanged.Price);
        Assert.True(unchanged.IsActive);
        Assert.Null(unchanged.UpdatedAt);
    }

    // --- Update ----------------------------------------------------------

    [Fact]
    public async Task Update_Returns204_AndChangesNameAndPrice()
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId, "Pregled", 2_000m);

        var response = await Admin(clinicId).PutAsJsonAsync($"/api/services/{id}", new { name = " Specijalisticki pregled ", price = 3_500m });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var saved = (await ReadServiceAsync(id))!;
        Assert.Equal("Specijalisticki pregled", saved.Name);
        Assert.Equal(3_500m, saved.Price);
        Assert.True(saved.IsActive);
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Update_Invalid_Returns400_AndChangesNothing(object body, string field)
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId, "Pregled", 2_000m);

        var response = await Admin(clinicId).PutAsJsonAsync($"/api/services/{id}", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty(field, out _));

        var unchanged = (await ReadServiceAsync(id))!;
        Assert.Equal("Pregled", unchanged.Name);
        Assert.Equal(2_000m, unchanged.Price);
    }

    [Fact]
    public async Task Update_Unknown_Returns404()
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PutAsJsonAsync($"/api/services/{Guid.NewGuid()}", new { name = "Pregled", price = 1m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A recorded service keeps the price it had: changing the catalog price
    // never rewrites history.
    [Fact]
    public async Task Update_DoesNotChangeThePriceOfAlreadyRecordedServices()
    {
        var clinicId = await SeedClinicAsync();
        var (userId, patientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Ana");
        var serviceId = await CreateAsync(clinicId, "Pregled", 2_000m);
        var admin = Client(UserRole.ClinicAdmin, clinicId, Guid.NewGuid());

        var recorded = await admin.PostAsJsonAsync($"/api/patients/{patientId}/services", new { id = Guid.NewGuid(), serviceId });
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);

        await admin.PutAsJsonAsync($"/api/services/{serviceId}", new { name = "Pregled", price = 9_000m });

        var history = Assert.Single(await TestDataSeeder.GetPatientServicesAsync(_factory, patientId));
        Assert.Equal(2_000m, history.PriceAtTimeOfService);
    }

    // --- Activation ------------------------------------------------------

    [Fact]
    public async Task Deactivate_ThenActivate_TogglesStatus_AndIsIdempotent()
    {
        var clinicId = await SeedClinicAsync();
        var id = await CreateAsync(clinicId);
        var admin = Admin(clinicId);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/services/{id}/deactivation", null)).StatusCode);
        Assert.False((await ReadServiceAsync(id))!.IsActive);

        var deactivatedAt = (await ReadServiceAsync(id))!.UpdatedAt;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/services/{id}/deactivation", null)).StatusCode);
        Assert.Equal(deactivatedAt, (await ReadServiceAsync(id))!.UpdatedAt);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/services/{id}/activation", null)).StatusCode);
        Assert.True((await ReadServiceAsync(id))!.IsActive);
    }

    [Theory]
    [InlineData("deactivation")]
    [InlineData("activation")]
    public async Task SetActive_Unknown_Returns404(string action)
    {
        var clinicId = await SeedClinicAsync();

        var response = await Admin(clinicId).PostAsync($"/api/services/{Guid.NewGuid()}/{action}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // BUSINESS.md §15: a deactivated service stays in the catalog but can no
    // longer be recorded for a patient; reactivating it allows it again.
    [Fact]
    public async Task DeactivatedService_CannotBeRecorded_UntilActivatedAgain()
    {
        var clinicId = await SeedClinicAsync();
        var (_, patientId) = await TestDataSeeder.SeedPatientWithUserAsync(_factory, clinicId, "Ana");
        var serviceId = await CreateAsync(clinicId, "Pregled", 2_000m);
        var admin = Client(UserRole.ClinicAdmin, clinicId, Guid.NewGuid());

        await admin.PostAsync($"/api/services/{serviceId}/deactivation", null);

        var rejected = await admin.PostAsJsonAsync($"/api/patients/{patientId}/services", new { id = Guid.NewGuid(), serviceId });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Empty(await TestDataSeeder.GetPatientServicesAsync(_factory, patientId));

        await admin.PostAsync($"/api/services/{serviceId}/activation", null);

        var recorded = await admin.PostAsJsonAsync($"/api/patients/{patientId}/services", new { id = Guid.NewGuid(), serviceId });
        Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);
    }
}
