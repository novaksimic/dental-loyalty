using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DentalLoyalty.Api.Configuration;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Security;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Security;

// The login rate limit (SECURITY.md §16): per client IP, a fixed window, 429 as
// ProblemDetails with Retry-After once exceeded - the same answer whatever the
// email - and no effect on any other endpoint. Forwarded client addresses are
// believed only from configured proxies.
//
// Each test builds its own host with a limit of 3, so limits never carry over.
public class LoginRateLimitTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const int Limit = 3;
    private const string Password = "Sup3rSecret!pass";

    private readonly DentalLoyaltyApiFactory _factory;

    public LoginRateLimitTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> LimitedHost(Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:Login:PermitLimit"] = Limit.ToString(),
            ["RateLimiting:Login:WindowSeconds"] = "60",
        };

        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));
    }

    private static async Task<string> SeedAdminAsync(WebApplicationFactory<Program> host)
    {
        var email = $"admin.{Guid.NewGuid():N}@platform.test";

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DentalLoyaltyDbContext>();
        db.Users.Add(new User(null, email, new PasswordHasher().Hash(Password), UserRole.PlatformAdmin));
        await db.SaveChangesAsync();

        return email;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginCommand(email, password));

    // A login sent with a chosen connection address (and optional
    // X-Forwarded-For), as a real server would see it.
    private static async Task<int> LoginFromAsync(
        WebApplicationFactory<Program> host,
        string remoteAddress,
        string? forwardedFor = null)
    {
        var context = await host.Server.SendAsync(c =>
        {
            c.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            c.Request.Method = HttpMethods.Post;
            c.Request.Path = "/api/auth/login";
            c.Request.ContentType = "application/json";
            c.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"email\":\"nobody@x.test\",\"password\":\"wrong-password-1\"}"));

            if (forwardedFor is not null)
            {
                c.Request.Headers["X-Forwarded-For"] = forwardedFor;
            }
        });

        return context.Response.StatusCode;
    }

    [Fact]
    public async Task AttemptsBeyondTheLimit_Get429ProblemDetails_WithRetryAfter()
    {
        var host = LimitedHost();
        var client = host.CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "nobody@x.test", "wrong-password-1")).StatusCode);
        }

        var rejected = await LoginAsync(client, "nobody@x.test", "wrong-password-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After must be set.");

        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, body.GetProperty("status").GetInt32());
        Assert.Equal("Too many requests. Try again later.", body.GetProperty("title").GetString());
    }

    // Once limited, valid credentials are refused too, and an existing and an
    // unknown email get the same answer - the limit reveals nothing.
    [Fact]
    public async Task OnceLimited_ExistingAndUnknownEmails_GetTheSameAnswer_EvenWithTheRightPassword()
    {
        var host = LimitedHost();
        var email = await SeedAdminAsync(host);
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email)).StatusCode);

        for (var i = 1; i < Limit; i++)
        {
            await LoginAsync(client, "nobody@x.test");
        }

        var existing = await LoginAsync(client, email);
        var unknown = await LoginAsync(client, "nobody@x.test");

        Assert.Equal(HttpStatusCode.TooManyRequests, existing.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, unknown.StatusCode);

        static async Task<string> Comparable(HttpResponseMessage response)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return $"{body.GetProperty("status")}|{body.GetProperty("title")}|{string.Join(",", body.EnumerateObject().Select(p => p.Name).Order())}";
        }

        Assert.Equal(await Comparable(existing), await Comparable(unknown));
        Assert.DoesNotContain(email, await existing.Content.ReadAsStringAsync());
    }

    // The limit applies to the login endpoint only.
    [Fact]
    public async Task OtherEndpoints_AreNotLimited()
    {
        var host = LimitedHost();
        var client = host.CreateClient();

        for (var i = 0; i <= Limit; i++)
        {
            await LoginAsync(client, "nobody@x.test");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, "nobody@x.test")).StatusCode);

        var admin = host.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.CreateValidToken(UserRole.PlatformAdmin));

        for (var i = 0; i < Limit * 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/clinics", new { name = $"Clinic {i}" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.CreateClient().GetAsync("/api/services")).StatusCode);
        }
    }

    // The limit is per client address: exhausting it for one does not affect another.
    [Fact]
    public async Task Limit_IsPerClientAddress()
    {
        var host = LimitedHost();

        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(StatusCodes.Status401Unauthorized, await LoginFromAsync(host, "203.0.113.10"));
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, await LoginFromAsync(host, "203.0.113.10"));
        Assert.Equal(StatusCodes.Status401Unauthorized, await LoginFromAsync(host, "203.0.113.11"));
    }

    // IPv4 and its IPv4-mapped IPv6 form are the same client.
    [Fact]
    public async Task IPv4MappedAddress_IsTheSameClient()
    {
        var host = LimitedHost();

        for (var i = 0; i < Limit; i++)
        {
            await LoginFromAsync(host, "203.0.113.20");
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, await LoginFromAsync(host, "::ffff:203.0.113.20"));
    }

    // Without configured proxies X-Forwarded-For is ignored: a client cannot
    // pick a new address per attempt to escape the limit.
    [Fact]
    public async Task ForwardedFor_IsIgnored_WhenNoProxyIsConfigured()
    {
        var host = LimitedHost();

        for (var i = 0; i < Limit; i++)
        {
            await LoginFromAsync(host, "203.0.113.30", forwardedFor: $"198.51.100.{i}");
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, await LoginFromAsync(host, "203.0.113.30", forwardedFor: "198.51.100.99"));
    }

    // Behind a configured proxy, the client address it forwards is used - and
    // only from that proxy; anyone else's X-Forwarded-For is still ignored.
    [Fact]
    public async Task ForwardedFor_IsUsed_OnlyFromAConfiguredProxy()
    {
        var host = LimitedHost(new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5" });

        for (var i = 0; i < Limit; i++)
        {
            await LoginFromAsync(host, "10.0.0.5", forwardedFor: "198.51.100.1");
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, await LoginFromAsync(host, "10.0.0.5", forwardedFor: "198.51.100.1"));
        Assert.Equal(StatusCodes.Status401Unauthorized, await LoginFromAsync(host, "10.0.0.5", forwardedFor: "198.51.100.2"));

        for (var i = 0; i < Limit; i++)
        {
            await LoginFromAsync(host, "203.0.113.40", forwardedFor: $"198.51.100.{50 + i}");
        }

        Assert.Equal(StatusCodes.Status429TooManyRequests, await LoginFromAsync(host, "203.0.113.40", forwardedFor: "198.51.100.99"));
    }

    // --- Configuration ------------------------------------------------------

    [Theory]
    [InlineData(0, 60)]
    [InlineData(10, 0)]
    [InlineData(10_001, 60)]
    [InlineData(10, 3_601)]
    public void Validator_RejectsOutOfRangeLimits(int permitLimit, int windowSeconds)
    {
        var result = new LoginRateLimitSettingsValidator().Validate(
            null,
            new LoginRateLimitSettings { PermitLimit = permitLimit, WindowSeconds = windowSeconds });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("not-an-ip", null)]
    [InlineData(null, "10.0.0.0")]
    [InlineData(null, "10.0.0.0/99")]
    public void ForwardedHeadersValidator_RejectsInvalidAddresses(string? proxy, string? network)
    {
        var settings = new ForwardedHeadersSettings
        {
            KnownProxies = proxy is null ? [] : [proxy],
            KnownNetworks = network is null ? [] : [network],
        };

        Assert.True(new ForwardedHeadersSettingsValidator().Validate(null, settings).Failed);
    }

    [Fact]
    public void InvalidLimitInConfiguration_FailsStartup()
    {
        var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:Login:PermitLimit"] = "0" })));

        var exception = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("RateLimiting:Login:PermitLimit", CorsTests.FindValidationFailure(exception).Message);
    }
}
