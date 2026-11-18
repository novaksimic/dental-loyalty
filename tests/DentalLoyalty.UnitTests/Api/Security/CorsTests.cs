using System.Net;
using System.Net.Http.Headers;
using DentalLoyalty.Api.Configuration;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DentalLoyalty.UnitTests.Api.Security;

// CORS for the Admin application (SECURITY.md §17): only configured origins,
// only the methods and headers the app uses, no credentials; preflight works
// without a token; and invalid origin configuration stops the application.
// The test host allows exactly TestOrigins.Allowed.
public class CorsTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public CorsTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static HttpRequestMessage Preflight(string path, string origin, string method = "GET", string? headers = "authorization,content-type")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);

        if (headers is not null)
        {
            request.Headers.Add("Access-Control-Request-Headers", headers);
        }

        return request;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    [Fact]
    public async Task Preflight_FromAllowedOrigin_IsAllowed_WithoutToken_AndWithoutCredentials()
    {
        var response = await _factory.CreateClient().SendAsync(Preflight("/api/services", TestOrigins.Allowed, "POST"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(TestOrigins.Allowed, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("POST", Header(response, "Access-Control-Allow-Methods"));
        Assert.Contains("authorization", Header(response, "Access-Control-Allow-Headers")!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
        Assert.NotNull(Header(response, "Access-Control-Max-Age"));
    }

    [Fact]
    public async Task Preflight_ForLogin_IsAllowed()
    {
        var response = await _factory.CreateClient().SendAsync(Preflight("/api/auth/login", TestOrigins.Allowed, "POST", "content-type"));

        Assert.Equal(TestOrigins.Allowed, Header(response, "Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData(TestOrigins.NotAllowed)]
    [InlineData("http://admin.test.example")]
    [InlineData("https://admin.test.example.evil.example")]
    [InlineData("null")]
    public async Task Preflight_FromOtherOrigin_IsNotAllowed(string origin)
    {
        var response = await _factory.CreateClient().SendAsync(Preflight("/api/services", origin));

        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
    }

    // ASP.NET Core answers a preflight with the policy's allowed methods and
    // headers and leaves the decision to the browser, which refuses a request
    // whose method or header is not listed. So the lists must contain exactly
    // what the Admin app uses.
    [Theory]
    [InlineData("PATCH")]
    [InlineData("TRACE")]
    public async Task Preflight_ForUnlistedMethod_DoesNotAllowIt(string method)
    {
        var response = await _factory.CreateClient().SendAsync(Preflight("/api/services", TestOrigins.Allowed, method));

        var allowed = Header(response, "Access-Control-Allow-Methods")!.Split(',', StringSplitOptions.TrimEntries);

        Assert.Equal(new[] { "DELETE", "GET", "POST", "PUT" }, allowed.Order());
        Assert.DoesNotContain(method, allowed);
    }

    [Fact]
    public async Task Preflight_ForUnlistedHeader_DoesNotAllowIt()
    {
        var response = await _factory.CreateClient().SendAsync(Preflight("/api/services", TestOrigins.Allowed, "GET", "authorization,x-custom"));

        var allowed = Header(response, "Access-Control-Allow-Headers")!
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant());

        Assert.Equal(new[] { "authorization", "content-type" }, allowed.Order());
    }

    // An actual request from the allowed origin gets the CORS headers the
    // browser needs, including the readable Location header.
    [Fact]
    public async Task Request_FromAllowedOrigin_GetsCorsHeaders()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic", 100m, 1_000);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/services");
        request.Headers.Add("Origin", TestOrigins.Allowed);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.CreateValidToken(UserRole.ClinicAdmin, clinicId));

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestOrigins.Allowed, Header(response, "Access-Control-Allow-Origin"));
        Assert.Contains("Location", Header(response, "Access-Control-Expose-Headers"));
        Assert.Null(Header(response, "Access-Control-Allow-Credentials"));
    }

    // The server still answers (CORS is not authorization); the browser
    // withholds the response because no CORS header allows it.
    [Fact]
    public async Task Request_FromOtherOrigin_GetsNoCorsHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/services");
        request.Headers.Add("Origin", TestOrigins.NotAllowed);

        var response = await _factory.CreateClient().SendAsync(request);

        Assert.Null(Header(response, "Access-Control-Allow-Origin"));
    }

    // --- Configuration -----------------------------------------------------

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("localhost:4200")]
    [InlineData("admin.example.com")]
    [InlineData("https://admin.example.com/")]
    [InlineData("https://admin.example.com/app")]
    [InlineData("https://admin.example.com?x=1")]
    [InlineData("https://user:pass@admin.example.com")]
    [InlineData("ftp://admin.example.com")]
    [InlineData("http://admin.example.com")]
    public void Validator_RejectsInvalidOrigins(string origin)
    {
        var result = new CorsSettingsValidator().Validate(null, new CorsSettings { AllowedOrigins = [origin] });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("https://admin.example.com")]
    [InlineData("https://admin.example.com:8443")]
    [InlineData("http://localhost:4200")]
    [InlineData("http://127.0.0.1:4200")]
    [InlineData("http://[::1]:4200")]
    public void Validator_AcceptsOrigins(string origin)
    {
        Assert.True(new CorsSettingsValidator().Validate(null, new CorsSettings { AllowedOrigins = [origin] }).Succeeded);
    }

    [Fact]
    public void Validator_AcceptsNoOrigins()
    {
        Assert.True(new CorsSettingsValidator().Validate(null, new CorsSettings()).Succeeded);
    }

    [Fact]
    public void Origins_AreNormalizedForComparison()
    {
        var settings = new CorsSettings { AllowedOrigins = [" HTTPS://Admin.Example.com ", "https://admin.example.com"] };

        Assert.Equal(new[] { "https://admin.example.com" }, settings.NormalizedOrigins());
    }

    // An invalid origin in configuration stops the application at startup.
    [Fact]
    public void WildcardOriginInConfiguration_FailsStartup()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "*" })));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins", FindValidationFailure(exception).Message);
    }

    internal static OptionsValidationException FindValidationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OptionsValidationException validation)
            {
                return validation;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    try
                    {
                        return FindValidationFailure(inner);
                    }
                    catch (Xunit.Sdk.XunitException)
                    {
                    }
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Expected an OptionsValidationException, got: {exception}");
    }
}
