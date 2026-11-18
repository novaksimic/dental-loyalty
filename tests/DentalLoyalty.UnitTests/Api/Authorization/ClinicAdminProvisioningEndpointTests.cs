using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// POST /api/clinics/{clinicId}/admins (ADR-010 decision 1) through the real
// pipeline: PlatformAdmin only; the clinic must exist; the email is normalized
// and unique platform-wide; the password satisfies PasswordPolicy and is stored
// hashed; a rejected request creates nothing; and the account can sign in.
public class ClinicAdminProvisioningEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string ValidPassword = "Sup3rSecret!pass";

    private readonly DentalLoyaltyApiFactory _factory;

    public ClinicAdminProvisioningEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static string Route(Guid clinicId) => $"/api/clinics/{clinicId}/admins";

    private static string UniqueEmail(string local = "admin") => $"{local}.{Guid.NewGuid():N}@clinic.test";

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

    private Task<Guid> SeedClinicAsync(string name = "Bright Smiles Dental") =>
        TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, name, 100m, 1_000);

    private Task<HttpResponseMessage> PostAsPlatformAdminAsync(Guid clinicId, object body) =>
        Client(UserRole.PlatformAdmin).PostAsJsonAsync(Route(clinicId), body);

    private async Task<List<User>> UsersWithEmailAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
        var normalized = email.Trim().ToLowerInvariant();

        return await db.Users.AsNoTracking()
            .Where(u => u.Email == normalized || u.Email == email)
            .ToListAsync();
    }

    private async Task<int> UserCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
        return await db.Users.CountAsync();
    }

    // --- Authorization ---------------------------------------------------

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var clinicId = await SeedClinicAsync();

        var response = await Client().PostAsJsonAsync(Route(clinicId), new { email = UniqueEmail(), password = ValidPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Even for the ClinicAdmin's own clinic: ClinicAdmins do not provision admins.
    [Fact]
    public async Task AsClinicAdmin_Returns403_AndCreatesNothing()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        var response = await Client(UserRole.ClinicAdmin, clinicId)
            .PostAsJsonAsync(Route(clinicId), new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await UsersWithEmailAsync(email));
    }

    [Fact]
    public async Task AsPatient_Returns403_AndCreatesNothing()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        var response = await Client(UserRole.Patient, clinicId)
            .PostAsJsonAsync(Route(clinicId), new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await UsersWithEmailAsync(email));
    }

    [Fact]
    public async Task AsPlatformAdmin_Returns201_WithAccountSummary_AndNoSecrets()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        var response = await PostAsPlatformAdminAsync(clinicId, new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        var user = Assert.Single(await UsersWithEmailAsync(email));

        Assert.Equal(user.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(clinicId, body.GetProperty("clinicId").GetGuid());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("ClinicAdmin", body.GetProperty("role").GetString());
        Assert.Equal(4, body.EnumerateObject().Count());
        Assert.DoesNotContain(ValidPassword, raw);
        Assert.DoesNotContain(user.PasswordHash, raw);
    }

    // --- Business behavior -----------------------------------------------

    [Fact]
    public async Task UnknownClinic_Returns404_AndCreatesNothing()
    {
        var email = UniqueEmail();

        var response = await PostAsPlatformAdminAsync(Guid.NewGuid(), new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await UsersWithEmailAsync(email));
    }

    [Fact]
    public async Task CreatedAccount_IsAnActiveClinicAdmin_OfTheRequestedClinic_WithHashedPassword()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        await PostAsPlatformAdminAsync(clinicId, new { email, password = ValidPassword });

        var user = Assert.Single(await UsersWithEmailAsync(email));

        Assert.Equal(UserRole.ClinicAdmin, user.Role);
        Assert.Equal(clinicId, user.ClinicId);
        Assert.True(user.IsActive);
        Assert.NotEqual(ValidPassword, user.PasswordHash);
        Assert.DoesNotContain(ValidPassword, user.PasswordHash);
        Assert.True(new PasswordHasher().Verify(ValidPassword, user.PasswordHash));
    }

    // The whole chain: the PlatformAdmin provisions the account, the ClinicAdmin
    // signs in with any casing of the email, and the issued token reaches the
    // clinic's own data - its role and clinic claim are the provisioned ones.
    [Fact]
    public async Task CreatedAccount_CanSignIn_AndReachItsOwnClinic()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail("Ana.Admin");

        var created = await PostAsPlatformAdminAsync(clinicId, new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var login = await Client().PostAsJsonAsync("/api/auth/login", new LoginCommand(email.ToUpperInvariant(), ValidPassword));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;

        Assert.Equal("ClinicAdmin", session.Role);
        Assert.Equal(clinicId, session.ClinicId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        var configuration = await client.GetFromJsonAsync<JsonElement>("/api/loyalty-configuration");

        Assert.Equal(clinicId, configuration.GetProperty("clinicId").GetGuid());
    }

    [Fact]
    public async Task CreatedAccount_WrongPassword_CannotSignIn()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        await PostAsPlatformAdminAsync(clinicId, new { email, password = ValidPassword });

        var login = await Client().PostAsJsonAsync("/api/auth/login", new LoginCommand(email, "Wrong-password-123"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Email_IsTrimmedAndLowerCased()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail("Ana.Admin").ToUpperInvariant();

        var response = await PostAsPlatformAdminAsync(clinicId, new { email = $"  {email}  ", password = ValidPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var user = Assert.Single(await UsersWithEmailAsync(email));

        Assert.Equal(email.ToLowerInvariant(), user.Email);
        Assert.Equal(email.ToLowerInvariant(), body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task DuplicateEmail_Returns409_AndCreatesNothing()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        await PostAsPlatformAdminAsync(clinicId, new { email, password = ValidPassword });
        var countBefore = await UserCountAsync();

        var response = await PostAsPlatformAdminAsync(clinicId, new { email, password = "Another-password-1" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(countBefore, await UserCountAsync());
    }

    [Fact]
    public async Task DuplicateEmail_InDifferentCasing_Returns409()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        await PostAsPlatformAdminAsync(clinicId, new { email, password = ValidPassword });

        var response = await PostAsPlatformAdminAsync(clinicId, new { email = $" {email.ToUpperInvariant()} ", password = ValidPassword });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await UsersWithEmailAsync(email));
    }

    // Users.Email is unique platform-wide: one email is one account in one clinic.
    [Fact]
    public async Task EmailOfAnotherClinicsAdmin_Returns409()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");
        var email = UniqueEmail();

        await PostAsPlatformAdminAsync(clinicA, new { email, password = ValidPassword });

        var response = await PostAsPlatformAdminAsync(clinicB, new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(clinicA, Assert.Single(await UsersWithEmailAsync(email)).ClinicId);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("admin@localhost")]
    [InlineData("Admin <admin@clinic.test>")]
    [InlineData("ad min@clinic.test")]
    public async Task InvalidEmail_Returns400_WithEmailError_AndCreatesNothing(string email)
    {
        var clinicId = await SeedClinicAsync();
        var countBefore = await UserCountAsync();

        var response = await PostAsPlatformAdminAsync(clinicId, new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var errors = JsonDocument.Parse(raw).RootElement.GetProperty("errors");

        Assert.True(errors.TryGetProperty("Email", out _));
        Assert.DoesNotContain(ValidPassword, raw);
        Assert.Equal(countBefore, await UserCountAsync());
    }

    [Theory]
    [InlineData("short")]
    [InlineData("123456789")]
    public async Task PasswordOutsidePolicy_Returns400_WithPasswordError_AndNeverEchoesIt(string password)
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        var response = await PostAsPlatformAdminAsync(clinicId, new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();

        Assert.True(JsonDocument.Parse(raw).RootElement.GetProperty("errors").TryGetProperty("Password", out _));
        Assert.DoesNotContain(password, raw);
        Assert.Empty(await UsersWithEmailAsync(email));
    }

    [Fact]
    public async Task PasswordEqualToEmail_Returns400()
    {
        var clinicId = await SeedClinicAsync();
        var email = UniqueEmail();

        var response = await PostAsPlatformAdminAsync(clinicId, new { email, password = email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await UsersWithEmailAsync(email));
    }

    [Fact]
    public async Task MissingFields_Return400()
    {
        var clinicId = await SeedClinicAsync();
        var countBefore = await UserCountAsync();

        var response = await PostAsPlatformAdminAsync(clinicId, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(countBefore, await UserCountAsync());
    }

    // --- Data integrity --------------------------------------------------

    // The clinic comes only from the route: a clinic id smuggled into the body
    // is ignored, and nothing is created in that other clinic.
    [Fact]
    public async Task ClinicIdInBody_IsIgnored_AccountBelongsToRouteClinic()
    {
        var routeClinic = await SeedClinicAsync("Route clinic");
        var otherClinic = await SeedClinicAsync("Other clinic");
        var email = UniqueEmail();

        var response = await PostAsPlatformAdminAsync(
            routeClinic,
            new { email, password = ValidPassword, clinicId = otherClinic });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = Assert.Single(await UsersWithEmailAsync(email));

        Assert.Equal(routeClinic, user.ClinicId);
        Assert.Equal(routeClinic, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clinicId").GetGuid());
    }
}
