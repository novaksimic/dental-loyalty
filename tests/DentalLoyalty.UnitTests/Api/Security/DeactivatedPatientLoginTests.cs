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
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Security;

// A deactivated patient cannot sign in (SECURITY.md §9.7): deactivating the
// patient (DELETE /api/patients/{id}) leaves the linked user account active, so
// login checks the patient record too - answering exactly like a wrong password.
public class DeactivatedPatientLoginTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string Password = "Sup3rSecret!pass";

    private readonly DentalLoyaltyApiFactory _factory;

    public DeactivatedPatientLoginTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Admin(Guid clinicId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateValidToken(UserRole.ClinicAdmin, clinicId));
        return client;
    }

    // A patient with an account created through the API, as in production.
    private async Task<(Guid ClinicId, Guid PatientId, string Email)> PatientWithAccountAsync()
    {
        var (clinicId, patientId) = await TestDataSeeder.SeedClinicWithPatientAsync(_factory, "Clinic", "Ana");
        var email = $"ana.{Guid.NewGuid():N}@patient.test";

        var created = await Admin(clinicId).PostAsJsonAsync($"/api/patients/{patientId}/account", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        return (clinicId, patientId, email);
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password = Password) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginCommand(email, password));

    private static async Task<string> Shape(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType?.MediaType}|{body.GetProperty("title")}|"
            + string.Join(",", body.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task ActivePatient_CanSignIn()
    {
        var (_, _, email) = await PatientWithAccountAsync();

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task DeactivatedPatient_CannotSignIn_AndGetsTheSameAnswerAsAWrongPassword()
    {
        var (clinicId, patientId, email) = await PatientWithAccountAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await Admin(clinicId).DeleteAsync($"/api/patients/{patientId}")).StatusCode);

        var deactivated = await LoginAsync(email);
        var wrongPassword = await LoginAsync(email, "Wrong-password-123");

        Assert.Equal(HttpStatusCode.Unauthorized, deactivated.StatusCode);
        Assert.Equal(await Shape(wrongPassword), await Shape(deactivated));
    }

    // The patient's user account itself is unchanged: only the patient record
    // is deactivated (the deactivation rules are not changed).
    [Fact]
    public async Task Deactivation_LeavesTheUserAccountActive()
    {
        var (clinicId, patientId, email) = await PatientWithAccountAsync();
        await Admin(clinicId).DeleteAsync($"/api/patients/{patientId}");

        using var scope = _factory.Services.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>().Users.Single(u => u.Email == email);

        Assert.True(user.IsActive);
    }

    // Other roles of the clinic are unaffected.
    [Fact]
    public async Task ClinicAdmin_OfTheClinic_StillSignsIn()
    {
        var (clinicId, patientId, _) = await PatientWithAccountAsync();
        await Admin(clinicId).DeleteAsync($"/api/patients/{patientId}");

        var adminEmail = $"admin.{Guid.NewGuid():N}@clinic.test";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
            db.Users.Add(new User(clinicId, adminEmail, new PasswordHasher().Hash(Password), UserRole.ClinicAdmin));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(adminEmail)).StatusCode);
    }
}
