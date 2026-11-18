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

// POST /api/patients/{id}/account (ADR-010 decision 2) through the real pipeline:
// ClinicAdmin only, for a patient of their own clinic (the clinic comes from the
// token, never the body); normalized, platform-unique email; PasswordPolicy and
// hashing; one account per patient; nothing saved on rejection; and the patient
// can sign in and reach their own record and loyalty state.
public class PatientAccountProvisioningEndpointTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string ValidPassword = "Sup3rSecret!pass";

    private readonly DentalLoyaltyApiFactory _factory;

    public PatientAccountProvisioningEndpointTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static string Route(Guid patientId) => $"/api/patients/{patientId}/account";

    private static string UniqueEmail(string local = "jane") => $"{local}.{Guid.NewGuid():N}@patient.test";

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

    private Task<(Guid ClinicId, Guid PatientId)> SeedClinicWithPatientAsync(string clinic = "Clinic A") =>
        TestDataSeeder.SeedClinicWithPatientAsync(_factory, clinic, "Jane");

    private Task<HttpResponseMessage> PostAsClinicAdminAsync(Guid clinicId, Guid patientId, object body) =>
        Client(UserRole.ClinicAdmin, clinicId).PostAsJsonAsync(Route(patientId), body);

    private async Task<(Patient Patient, List<User> Users, List<LoyaltyCycle> Cycles)> ReadStateAsync(Guid patientId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();

        var patient = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == patientId);
        var users = patient.UserId is null
            ? []
            : await db.Users.AsNoTracking().Where(u => u.Id == patient.UserId).ToListAsync();
        var cycles = await db.LoyaltyCycles.AsNoTracking().Where(c => c.PatientId == patientId).ToListAsync();

        return (patient, users, cycles);
    }

    private async Task<int> UserCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>().Users.CountAsync();
    }

    private async Task AssertUnlinkedAsync(Guid patientId)
    {
        var state = await ReadStateAsync(patientId);

        Assert.Null(state.Patient.UserId);
        Assert.Empty(state.Cycles);
    }

    // --- Authorization ---------------------------------------------------

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var (_, patientId) = await SeedClinicWithPatientAsync();

        var response = await Client().PostAsJsonAsync(Route(patientId), new { email = UniqueEmail(), password = ValidPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.PlatformAdmin)]
    [InlineData(UserRole.Patient)]
    public async Task OtherRoles_Return403_AndCreateNothing(UserRole role)
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();

        var response = await Client(role, role == UserRole.PlatformAdmin ? null : clinicId)
            .PostAsJsonAsync(Route(patientId), new { email = UniqueEmail(), password = ValidPassword });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertUnlinkedAsync(patientId);
    }

    // --- Success ---------------------------------------------------------

    [Fact]
    public async Task ClinicAdmin_Returns201_WithAccountSummary_AndNoSecrets()
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();
        var email = UniqueEmail();

        var response = await PostAsClinicAdminAsync(clinicId, patientId, new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        var state = await ReadStateAsync(patientId);
        var user = Assert.Single(state.Users);

        Assert.Equal(user.Id, body.GetProperty("userId").GetGuid());
        Assert.Equal(patientId, body.GetProperty("patientId").GetGuid());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Patient", body.GetProperty("role").GetString());
        Assert.Equal(4, body.EnumerateObject().Count());
        Assert.DoesNotContain(ValidPassword, raw);
        Assert.DoesNotContain(user.PasswordHash, raw);
    }

    // The user is a Patient of the patient's clinic, linked to the record, with
    // a hashed password and a normalized email; the link starts the first cycle.
    [Fact]
    public async Task CreatedAccount_IsLinkedPatientUser_WithHashedPassword_AndFirstCycle()
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();
        var email = UniqueEmail("Jane.Doe").ToUpperInvariant();

        await PostAsClinicAdminAsync(clinicId, patientId, new { email = $" {email} ", password = ValidPassword });

        var state = await ReadStateAsync(patientId);
        var user = Assert.Single(state.Users);

        Assert.Equal(user.Id, state.Patient.UserId);
        Assert.Equal(UserRole.Patient, user.Role);
        Assert.Equal(clinicId, user.ClinicId);
        Assert.True(user.IsActive);
        Assert.Equal(email.ToLowerInvariant(), user.Email);
        Assert.NotEqual(ValidPassword, user.PasswordHash);
        Assert.DoesNotContain(ValidPassword, user.PasswordHash);
        Assert.True(new PasswordHasher().Verify(ValidPassword, user.PasswordHash));

        var cycle = Assert.Single(state.Cycles);
        Assert.Equal(LoyaltyCycleStatus.Active, cycle.Status);
    }

    // The whole chain: the ClinicAdmin creates the account, the patient signs in
    // with any casing, and the token reaches the patient's own record and
    // loyalty state - now with an active cycle.
    [Fact]
    public async Task Patient_CanSignIn_AndReachTheirOwnRecord()
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();
        var email = UniqueEmail();

        var created = await PostAsClinicAdminAsync(clinicId, patientId, new { email, password = ValidPassword });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var login = await Client().PostAsJsonAsync("/api/auth/login", new LoginCommand(email.ToUpperInvariant(), ValidPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal("Patient", session.Role);
        Assert.Equal(clinicId, session.ClinicId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/patients/me");
        Assert.Equal(patientId, me.GetProperty("id").GetGuid());

        var history = await client.GetFromJsonAsync<JsonElement>("/api/patients/me/loyalty-history");
        Assert.Equal(JsonValueKind.Object, history.GetProperty("status").GetProperty("activeCycle").ValueKind);
    }

    // --- Tenant isolation ------------------------------------------------

    // A ClinicAdmin of clinic A cannot reach clinic B's patient - not even by
    // claiming clinic B in the body. Indistinguishable from no patient (404).
    [Fact]
    public async Task PatientOfAnotherClinic_Returns404_AndStaysUnlinked()
    {
        var (clinicA, _) = await SeedClinicWithPatientAsync("Clinic A");
        var (clinicB, patientOfB) = await SeedClinicWithPatientAsync("Clinic B");
        var countBefore = await UserCountAsync();

        var response = await PostAsClinicAdminAsync(
            clinicA,
            patientOfB,
            new { email = UniqueEmail(), password = ValidPassword, clinicId = clinicB });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUnlinkedAsync(patientOfB);
        Assert.Equal(countBefore, await UserCountAsync());
    }

    [Fact]
    public async Task UnknownPatient_Returns404()
    {
        var (clinicId, _) = await SeedClinicWithPatientAsync();

        var response = await PostAsClinicAdminAsync(clinicId, Guid.NewGuid(), new { email = UniqueEmail(), password = ValidPassword });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A ClinicAdmin token without a clinic claim fails closed.
    [Fact]
    public async Task ClinicAdminWithoutClinicClaim_Returns404()
    {
        var (_, patientId) = await SeedClinicWithPatientAsync();

        var response = await Client(UserRole.ClinicAdmin).PostAsJsonAsync(Route(patientId), new { email = UniqueEmail(), password = ValidPassword });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUnlinkedAsync(patientId);
    }

    // --- Rejections ------------------------------------------------------

    [Fact]
    public async Task PatientAlreadyLinked_Returns409_AndKeepsTheExistingAccount()
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();

        await PostAsClinicAdminAsync(clinicId, patientId, new { email = UniqueEmail(), password = ValidPassword });
        var before = await ReadStateAsync(patientId);

        var response = await PostAsClinicAdminAsync(clinicId, patientId, new { email = UniqueEmail("intruder"), password = ValidPassword });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var after = await ReadStateAsync(patientId);
        Assert.Equal(before.Patient.UserId, after.Patient.UserId);
        Assert.Single(after.Cycles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateEmail_Returns409_InAnyCasing_AndLeavesPatientUnlinked(bool differentCasing)
    {
        var (clinicId, firstPatient) = await SeedClinicWithPatientAsync();
        var secondPatient = await TestDataSeeder.SeedUnlinkedPatientAsync(_factory, clinicId);
        var email = UniqueEmail();

        await PostAsClinicAdminAsync(clinicId, firstPatient, new { email, password = ValidPassword });

        var response = await PostAsClinicAdminAsync(
            clinicId,
            secondPatient,
            new { email = differentCasing ? email.ToUpperInvariant() : email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertUnlinkedAsync(secondPatient);
    }

    // Users.Email is unique platform-wide: a ClinicAdmin's email cannot be reused
    // for a patient account.
    [Fact]
    public async Task EmailOfAClinicAdmin_Returns409()
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();
        var email = UniqueEmail("admin");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
            db.Users.Add(new User(clinicId, email, "hash", UserRole.ClinicAdmin));
            await db.SaveChangesAsync();
        }

        var response = await PostAsClinicAdminAsync(clinicId, patientId, new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertUnlinkedAsync(patientId);
    }

    [Fact]
    public async Task InactivePatient_Returns409_AndStaysUnlinked()
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();
        await Client(UserRole.ClinicAdmin, clinicId).DeleteAsync($"/api/patients/{patientId}");

        var response = await PostAsClinicAdminAsync(clinicId, patientId, new { email = UniqueEmail(), password = ValidPassword });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertUnlinkedAsync(patientId);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("jane@localhost")]
    [InlineData("Jane <jane@patient.test>")]
    public async Task InvalidEmail_Returns400_WithEmailError_AndCreatesNothing(string email)
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();

        var response = await PostAsClinicAdminAsync(clinicId, patientId, new { email, password = ValidPassword });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(JsonDocument.Parse(raw).RootElement.GetProperty("errors").TryGetProperty("Email", out _));
        Assert.DoesNotContain(ValidPassword, raw);
        await AssertUnlinkedAsync(patientId);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("123456789")]
    public async Task PasswordOutsidePolicy_Returns400_WithPasswordError_AndNeverEchoesIt(string password)
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();

        var response = await PostAsClinicAdminAsync(clinicId, patientId, new { email = UniqueEmail(), password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(JsonDocument.Parse(raw).RootElement.GetProperty("errors").TryGetProperty("Password", out _));
        Assert.DoesNotContain(password, raw);
        await AssertUnlinkedAsync(patientId);
    }

    [Fact]
    public async Task MissingFields_Return400_AndCreateNothing()
    {
        var (clinicId, patientId) = await SeedClinicWithPatientAsync();

        var response = await PostAsClinicAdminAsync(clinicId, patientId, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnlinkedAsync(patientId);
    }
}
