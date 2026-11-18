using System.Net;
using System.Net.Http.Json;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Security;

// The Jwt configuration is validated at startup (JwtOptionsValidator): a
// missing or weak value stops the application with an error that names the key
// but never reveals its value. Token validation itself is unchanged.
public class JwtConfigurationTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string ValidKey = "jwt-configuration-tests-signing-key-0123456789";

    private readonly DentalLoyaltyApiFactory _factory;

    public JwtConfigurationTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static JwtOptions Options(
        string signingKey = ValidKey,
        string issuer = "DentalLoyalty",
        string audience = "DentalLoyalty.Client",
        int lifetime = 60) => new()
    {
        SigningKey = signingKey,
        Issuer = issuer,
        Audience = audience,
        AccessTokenLifetimeMinutes = lifetime,
    };

    // --- Validator ------------------------------------------------------------

    [Fact]
    public void ValidConfiguration_Passes()
    {
        Assert.True(new JwtOptionsValidator().Validate(null, Options()).Succeeded);
    }

    [Fact]
    public void SigningKeyOfExactly32Bytes_Passes()
    {
        Assert.True(new JwtOptionsValidator().Validate(null, Options(signingKey: new string('k', 32))).Succeeded);
    }

    [Theory]
    [InlineData("", "Jwt:SigningKey")]
    [InlineData("   ", "Jwt:SigningKey")]
    [InlineData("short-but-not-32-bytes-long-key", "Jwt:SigningKey")]
    public void MissingOrShortSigningKey_Fails_WithoutRevealingIt(string key, string expectedKey)
    {
        var result = new JwtOptionsValidator().Validate(null, Options(signingKey: key));

        Assert.True(result.Failed);
        Assert.Contains(expectedKey, result.FailureMessage);

        if (key.Trim().Length > 0)
        {
            Assert.DoesNotContain(key, result.FailureMessage);
        }
    }

    [Fact]
    public void MissingIssuerAndAudience_Fail()
    {
        var result = new JwtOptionsValidator().Validate(null, Options(issuer: "", audience: " "));

        Assert.Contains("Jwt:Issuer", result.FailureMessage);
        Assert.Contains("Jwt:Audience", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(JwtOptionsValidator.MaximumAccessTokenLifetimeMinutes + 1)]
    public void LifetimeOutOfRange_Fails(int lifetime)
    {
        Assert.Contains(
            "Jwt:AccessTokenLifetimeMinutes",
            new JwtOptionsValidator().Validate(null, Options(lifetime: lifetime)).FailureMessage);
    }

    // --- Startup ----------------------------------------------------------------

    private WebApplicationFactory<Program> WithJwt(Dictionary<string, string?> overrides) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides)));

    [Theory]
    [InlineData("Jwt:SigningKey", "")]
    [InlineData("Jwt:SigningKey", "tiny-secret-value")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    public void InvalidJwtConfiguration_FailsStartup_NamingTheKey(string key, string value)
    {
        var factory = WithJwt(new Dictionary<string, string?> { [key] = value });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var failure = CorsTests.FindValidationFailure(exception);

        Assert.Contains(key, failure.Message);

        if (value.Length > 0)
        {
            Assert.DoesNotContain(value, exception.ToString());
        }
    }

    // A minimum-length (256-bit) key works end to end: tokens are issued and
    // accepted.
    [Fact]
    public async Task MinimumLengthKey_IssuesAndAcceptsTokens()
    {
        var key = new string('k', JwtOptionsValidator.MinimumSigningKeyBytes);
        var factory = WithJwt(new Dictionary<string, string?> { ["Jwt:SigningKey"] = key });

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
            db.Users.Add(new User(null, "minkey@platform.test", new PasswordHasher().Hash("Sup3rSecret!pass"), UserRole.PlatformAdmin));
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginCommand("minkey@platform.test", "Sup3rSecret!pass"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var created = await client.PostAsJsonAsync("/api/clinics", new { name = "Clinic" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    // Committed configuration never carries a signing key: it must come from
    // user secrets (Development) or the environment / a secret store.
    [Fact]
    public void CommittedAppsettings_HaveNoSigningKey()
    {
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, file);
            var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

            Assert.True(string.IsNullOrEmpty(configuration["Jwt:SigningKey"]), $"{file} must not contain Jwt:SigningKey.");
        }
    }
}
