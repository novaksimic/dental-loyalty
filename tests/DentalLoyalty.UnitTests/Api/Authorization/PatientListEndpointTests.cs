using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// GET /api/patients through the real pipeline: ClinicAdmin only (PlatformAdmin
// is denied patient data, SECURITY.md §7.12); only the caller's own clinic's
// patients; search, status filter and paging with consistent metadata; 400 for
// invalid paging; and list items that carry no account or internal data.
// Each test uses its own clinic, so counts are exact.
public class PatientListEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public PatientListEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(UserRole? role = null, Guid? clinicId = null)
    {
        var client = _factory.CreateClient();

        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", TestJwt.CreateValidToken(role.Value, clinicId));
        }

        return client;
    }

    private Task<Guid> SeedClinicAsync(string name = "Clinic") =>
        TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, name, 100m, 1_000);

    private async Task<Patient> SeedPatientAsync(
        Guid clinicId,
        string firstName,
        string lastName,
        string phone = "+381600000000",
        string? email = null,
        bool active = true,
        bool withAccount = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var patient = new Patient(clinicId, firstName, lastName, phone, email);

        if (withAccount)
        {
            var user = new User(clinicId, $"{Guid.NewGuid():N}@account.test", "secret-hash-value", UserRole.Patient);
            patient.LinkUser(user.Id);
            db.Users.Add(user);
        }

        if (!active)
        {
            patient.Deactivate();
        }

        db.Patients.Add(patient);
        db.LoyaltyAccounts.Add(LoyaltyAccount.OpenFor(patient));
        await db.SaveChangesAsync();

        return patient;
    }

    private async Task<JsonElement> ListAsync(Guid clinicId, string query = "")
    {
        var response = await Client(UserRole.ClinicAdmin, clinicId).GetAsync($"/api/patients{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string[] LastNames(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("lastName").GetString()!).ToArray();

    // --- Authorization ---------------------------------------------------

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/patients")).StatusCode);
    }

    [Fact]
    public async Task AsPlatformAdmin_Returns403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(UserRole.PlatformAdmin).GetAsync("/api/patients")).StatusCode);
    }

    [Fact]
    public async Task AsPatient_Returns403()
    {
        var clinicId = await SeedClinicAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Client(UserRole.Patient, clinicId).GetAsync("/api/patients")).StatusCode);
    }

    [Fact]
    public async Task ClinicAdminWithoutClinicClaim_Returns403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(UserRole.ClinicAdmin).GetAsync("/api/patients")).StatusCode);
    }

    // --- Tenant isolation ------------------------------------------------

    [Fact]
    public async Task ListsOnlyTheCallersClinic_EvenWhenSearchingForAnotherClinicsPatient()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");
        await SeedPatientAsync(clinicA, "Ana", "Mine");
        await SeedPatientAsync(clinicB, "Ana", "Theirs", email: "theirs@b.test");

        var all = await ListAsync(clinicA);
        var searched = await ListAsync(clinicA, "?search=theirs");

        Assert.Equal(new[] { "Mine" }, LastNames(all));
        Assert.Equal(1, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, searched.GetProperty("totalCount").GetInt32());
    }

    // A clinic id in the query string is not a parameter: it cannot select
    // another clinic.
    [Fact]
    public async Task ClinicIdInQuery_IsIgnored()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");
        await SeedPatientAsync(clinicB, "Ana", "Theirs");

        var page = await ListAsync(clinicA, $"?clinicId={clinicB}");

        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    // --- Response shape --------------------------------------------------

    [Fact]
    public async Task Response_HasPageMetadata_AndItemsWithoutAccountOrInternalData()
    {
        var clinicId = await SeedClinicAsync();
        var linked = await SeedPatientAsync(clinicId, "Ana", "Petrovic", "+381641112233", "ana@mail.test", withAccount: true);
        await SeedPatientAsync(clinicId, "Marko", "Zoric");

        var response = await Client(UserRole.ClinicAdmin, clinicId).GetAsync("/api/patients");
        var raw = await response.Content.ReadAsStringAsync();
        var page = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(
            new[] { "items", "page", "pageSize", "totalCount", "totalPages" },
            page.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());

        var first = page.GetProperty("items")[0];
        Assert.Equal(
            new[] { "email", "firstName", "hasAccount", "id", "isActive", "lastName", "phoneNumber" },
            first.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(linked.Id, first.GetProperty("id").GetGuid());
        Assert.Equal("Ana", first.GetProperty("firstName").GetString());
        Assert.Equal("+381641112233", first.GetProperty("phoneNumber").GetString());
        Assert.Equal("ana@mail.test", first.GetProperty("email").GetString());
        Assert.True(first.GetProperty("isActive").GetBoolean());
        Assert.True(first.GetProperty("hasAccount").GetBoolean());
        Assert.False(page.GetProperty("items")[1].GetProperty("hasAccount").GetBoolean());

        Assert.DoesNotContain("secret-hash-value", raw);
        Assert.DoesNotContain("@account.test", raw);
        Assert.DoesNotContain(linked.UserId!.Value.ToString(), raw);
        Assert.DoesNotContain(clinicId.ToString(), raw);
    }

    [Fact]
    public async Task NoPatients_IsAnEmptyPage()
    {
        var clinicId = await SeedClinicAsync();

        var page = await ListAsync(clinicId);

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, page.GetProperty("totalPages").GetInt32());
    }

    // --- Pagination ------------------------------------------------------

    [Fact]
    public async Task Pages_AreOrderedByName_WithTotals()
    {
        var clinicId = await SeedClinicAsync();

        foreach (var lastName in new[] { "E", "C", "A", "D", "B" })
        {
            await SeedPatientAsync(clinicId, "Ana", lastName);
        }

        var first = await ListAsync(clinicId, "?page=1&pageSize=2");
        var last = await ListAsync(clinicId, "?page=3&pageSize=2");
        var beyond = await ListAsync(clinicId, "?page=4&pageSize=2");

        Assert.Equal(new[] { "A", "B" }, LastNames(first));
        Assert.Equal(new[] { "E" }, LastNames(last));
        Assert.Empty(LastNames(beyond));

        foreach (var page in new[] { first, last, beyond })
        {
            Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
            Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        }

        Assert.Equal(3, last.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task MaximumPageSize_IsAccepted()
    {
        var clinicId = await SeedClinicAsync();

        var page = await ListAsync(clinicId, "?pageSize=100");

        Assert.Equal(100, page.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("?page=0", "page")]
    [InlineData("?page=-1", "page")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?pageSize=-3", "pageSize")]
    [InlineData("?page=abc", "page")]
    [InlineData("?page=2147483647&pageSize=100", "page")]
    public async Task InvalidPaging_Returns400_NamingTheParameter(string query, string parameter)
    {
        var clinicId = await SeedClinicAsync();

        var response = await Client(UserRole.ClinicAdmin, clinicId).GetAsync($"/api/patients{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals(parameter, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchTooLong_Returns400()
    {
        var clinicId = await SeedClinicAsync();

        var response = await Client(UserRole.ClinicAdmin, clinicId).GetAsync($"/api/patients?search={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Search and filtering --------------------------------------------

    [Theory]
    [InlineData("jovana")]
    [InlineData("JOVANA")]
    [InlineData("Nikol")]
    [InlineData("NIKOLIC")]
    [InlineData("jn%40mail")]
    [InlineData("JN%40MAIL.TEST")]
    [InlineData("641112")]
    public async Task Search_MatchesEachField_CaseInsensitively(string search)
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Jovana", "Nikolic", "+381641112233", "jn@mail.test");
        await SeedPatientAsync(clinicId, "Marko", "Markovic", "+381609998877", "marko@mail.test");

        var page = await ListAsync(clinicId, $"?search={search}");

        Assert.Equal(new[] { "Nikolic" }, LastNames(page));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_WithFirstAndLastName_RequiresBoth()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic");
        await SeedPatientAsync(clinicId, "Ana", "Ilic");
        await SeedPatientAsync(clinicId, "Marko", "Petrovic");

        var page = await ListAsync(clinicId, "?search=ana%20petrovic");

        var only = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("Ana", only.GetProperty("firstName").GetString());
        Assert.Equal("Petrovic", only.GetProperty("lastName").GetString());
    }

    [Fact]
    public async Task BlankSearch_ListsEveryone()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic");
        await SeedPatientAsync(clinicId, "Marko", "Ilic");

        Assert.Equal(2, (await ListAsync(clinicId, "?search=%20%20")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_WithNoMatch_IsAnEmptyPage()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic");

        var page = await ListAsync(clinicId, "?search=nobody");

        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task FiltersByStatus_AndCombinesWithSearch()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Active");
        await SeedPatientAsync(clinicId, "Ana", "Inactive", active: false);
        await SeedPatientAsync(clinicId, "Marko", "AlsoInactive", active: false);

        Assert.Equal(new[] { "Active" }, LastNames(await ListAsync(clinicId, "?isActive=true")));
        Assert.Equal(new[] { "AlsoInactive", "Inactive" }, LastNames(await ListAsync(clinicId, "?isActive=false")));
        Assert.Equal(new[] { "Inactive" }, LastNames(await ListAsync(clinicId, "?isActive=false&search=ana")));
        Assert.Equal(3, (await ListAsync(clinicId)).GetProperty("totalCount").GetInt32());
    }

    // A patient deactivated through the API is listed as inactive.
    [Fact]
    public async Task DeactivatedPatient_IsListedAsInactive()
    {
        var clinicId = await SeedClinicAsync();
        var patient = await SeedPatientAsync(clinicId, "Ana", "Petrovic");

        await Client(UserRole.ClinicAdmin, clinicId).DeleteAsync($"/api/patients/{patient.Id}");

        var page = await ListAsync(clinicId);
        Assert.False(page.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
    }
}
