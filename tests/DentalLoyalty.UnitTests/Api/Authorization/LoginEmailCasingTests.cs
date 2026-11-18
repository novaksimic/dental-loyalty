using System.Net;
using System.Net.Http.Json;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// POST /api/auth/login through the real pipeline with the EF in-memory provider,
// which compares strings case-sensitively (ADR-010 decision 7).
public class LoginEmailCasingTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string Password = "Sup3rSecret!pass";

    private readonly DentalLoyaltyApiFactory _factory;

    public LoginEmailCasingTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedClinicAdminAsync(string email)
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic A", 100m, 1_000);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
        db.Users.Add(new User(clinicId, email, new PasswordHasher().Hash(Password), UserRole.ClinicAdmin));
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> LoginAsync(string email, string password = Password) =>
        _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginCommand(email, password));

    private static string Unique(string local, string domain = "Example.com") => $"{local}.{Guid.NewGuid():N}@{domain}";

    [Fact]
    public async Task MixedCaseAccount_SignsIn_WithItsStoredCasing_AndSurroundingSpaces()
    {
        var email = Unique("Ana.Admin");
        await SeedClinicAdminAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync($"  {email}  ")).StatusCode);
    }

    // Known compatibility limitation (ADR-010 decision 7): a case-sensitive provider does
    // not find an existing mixed-case account with a different casing.
    [Fact]
    public async Task MixedCaseAccount_DifferentCasing_Returns401_OnACaseSensitiveProvider()
    {
        var email = Unique("Ana.Admin");
        await SeedClinicAdminAsync(email);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email.ToLowerInvariant())).StatusCode);
    }

    [Fact]
    public async Task LowerCaseAccount_SignsIn_WithAnyCasing()
    {
        var email = Unique("ana", "example.com").ToLowerInvariant();
        await SeedClinicAdminAsync(email);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email.ToUpperInvariant())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync($"  {email}  ")).StatusCode);
    }

    [Fact]
    public async Task LowerCaseAccount_WrongPassword_Returns401()
    {
        var email = Unique("ana", "example.com").ToLowerInvariant();
        await SeedClinicAdminAsync(email);

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email.ToUpperInvariant(), "wrong-password-1")).StatusCode);
    }
}
