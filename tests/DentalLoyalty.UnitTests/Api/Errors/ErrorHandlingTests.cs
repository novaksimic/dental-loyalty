using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DentalLoyalty.Api.Errors;
using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using DentalLoyalty.Domain.Exceptions;
using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DentalLoyalty.UnitTests.Api.Errors;

// Centralized error handling through the real pipeline: every error status is a
// ProblemDetails (application/problem+json) with a traceId; intentional status
// codes are unchanged; exceptions that escape a controller are mapped to a
// generic response that never carries the exception's message, type or stack;
// and unexpected exceptions are logged with the trace id.
public class ErrorHandlingTests : IClassFixture<DentalLoyaltyApiFactory>
{
    // What a leaked internal error could look like: SQL, a connection string, a value.
    private const string SecretDetail =
        "Violation of UNIQUE KEY constraint 'IX_Users_Email'. Server=prod-sql;Password=hunter2; value secret@clinic.test";

    private readonly DentalLoyaltyApiFactory _factory;

    public ErrorHandlingTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    private static HttpClient WithToken(HttpClient client, UserRole role, Guid? clinicId = null)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.CreateValidToken(role, clinicId));
        return client;
    }

    // A host whose clinic repository throws the given exception, with its logs captured.
    private (WebApplicationFactory<Program> Factory, CapturingLoggerProvider Logs) Throwing(Exception exception, string environment = "Testing")
    {
        var logs = new CapturingLoggerProvider();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IClinicRepository>(_ => new ThrowingClinicRepository(exception));
                services.AddSingleton<ILoggerProvider>(logs);
            });
        });

        return (factory, logs);
    }

    private static async Task<(JsonElement Body, string Raw)> ReadProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        Assert.Equal((int)response.StatusCode, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));

        return (body, raw);
    }

    private static void AssertNoInternals(string raw)
    {
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("prod-sql", raw);
        Assert.DoesNotContain("IX_Users_Email", raw);
        Assert.DoesNotContain("secret@clinic.test", raw);
        Assert.DoesNotContain("Exception", raw);
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at DentalLoyalty", raw);
        Assert.DoesNotContain(".cs:line", raw);
    }

    // --- Intentional responses keep their status, now as ProblemDetails ------

    [Fact]
    public async Task ValidationError_Is400ProblemDetails_WithFieldErrors()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic", 100m, 1_000);

        var response = await WithToken(_factory.CreateClient(), UserRole.ClinicAdmin, clinicId)
            .PostAsJsonAsync("/api/services", new { name = " ", price = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (body, _) = await ReadProblemAsync(response);
        Assert.True(body.GetProperty("errors").TryGetProperty("Name", out _));
    }

    // A malformed body is a 400 that does not echo the parser's internals.
    [Fact]
    public async Task MalformedJson_Is400ProblemDetails()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic", 100m, 1_000);
        var content = new StringContent("{ \"name\": \"hunter2\", \"price\": ", Encoding.UTF8, "application/json");

        var response = await WithToken(_factory.CreateClient(), UserRole.ClinicAdmin, clinicId).PostAsync("/api/services", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var (_, raw) = await ReadProblemAsync(response);
        Assert.DoesNotContain("hunter2", raw);
    }

    [Fact]
    public async Task ResourceNotFound_Is404ProblemDetails()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic", 100m, 1_000);

        var response = await WithToken(_factory.CreateClient(), UserRole.ClinicAdmin, clinicId).GetAsync($"/api/services/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await ReadProblemAsync(response);
    }

    [Fact]
    public async Task UnknownRoute_Is404ProblemDetails()
    {
        var response = await _factory.CreateClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await ReadProblemAsync(response);
    }

    [Fact]
    public async Task Unauthenticated_Is401ProblemDetails()
    {
        var response = await _factory.CreateClient().GetAsync("/api/services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await ReadProblemAsync(response);
    }

    [Fact]
    public async Task Forbidden_Is403ProblemDetails()
    {
        var response = await WithToken(_factory.CreateClient(), UserRole.PlatformAdmin).GetAsync("/api/services");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ReadProblemAsync(response);
    }

    // A business conflict answered by a controller keeps its specific title.
    [Fact]
    public async Task ControllerConflict_Is409ProblemDetails_WithItsTitle()
    {
        var clinicId = await TestDataSeeder.SeedClinicWithLoyaltyConfigurationAsync(_factory, "Clinic", 100m, 1_000);
        var client = WithToken(_factory.CreateClient(), UserRole.PlatformAdmin);
        var body = new { email = $"admin.{Guid.NewGuid():N}@clinic.test", password = "Sup3rSecret!pass" };

        await client.PostAsJsonAsync($"/api/clinics/{clinicId}/admins", body);
        var response = await client.PostAsJsonAsync($"/api/clinics/{clinicId}/admins", body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var (problem, _) = await ReadProblemAsync(response);
        Assert.Equal("A user with this email already exists.", problem.GetProperty("title").GetString());
    }

    // --- Exceptions that escape a controller ----------------------------------

    public static TheoryData<Exception, HttpStatusCode> KnownExceptions() => new()
    {
        { new ConcurrencyConflictException(SecretDetail, new Exception(SecretDetail)), HttpStatusCode.Conflict },
        { new DuplicateEmailException(SecretDetail, new Exception(SecretDetail)), HttpStatusCode.Conflict },
        { new DomainException(SecretDetail), HttpStatusCode.Conflict },
        { new ArgumentOutOfRangeException("price", SecretDetail), HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(KnownExceptions))]
    public async Task KnownException_IsMappedToItsStatus_WithAGenericTitle(Exception exception, HttpStatusCode expected)
    {
        var (factory, logs) = Throwing(exception);

        var response = await WithToken(factory.CreateClient(), UserRole.PlatformAdmin).PostAsJsonAsync("/api/clinics", new { name = "Clinic" });

        Assert.Equal(expected, response.StatusCode);
        var (body, raw) = await ReadProblemAsync(response);
        Assert.Equal(ApiExceptionHandler.Map(exception).Title, body.GetProperty("title").GetString());
        AssertNoInternals(raw);

        // A client-caused error is not logged as an error.
        Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
    }

    public static TheoryData<Exception> UnexpectedExceptions() => new()
    {
        new InvalidOperationException(SecretDetail),
        new DbUpdateException(SecretDetail, new Exception(SecretDetail)),
        new NullReferenceException(SecretDetail),
        new ArgumentNullException("user", SecretDetail),
    };

    [Theory]
    [MemberData(nameof(UnexpectedExceptions))]
    public async Task UnexpectedException_Is500_WithAGenericMessage_AndNoInternals(Exception exception)
    {
        var (factory, _) = Throwing(exception);

        var response = await WithToken(factory.CreateClient(), UserRole.PlatformAdmin).PostAsJsonAsync("/api/clinics", new { name = "Clinic" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var (body, raw) = await ReadProblemAsync(response);
        Assert.Equal("An unexpected error occurred.", body.GetProperty("title").GetString());
        Assert.False(body.TryGetProperty("detail", out _));
        AssertNoInternals(raw);
    }

    // The developer exception page must not take over in Development either.
    [Fact]
    public async Task UnexpectedException_InDevelopment_StillHidesInternals()
    {
        var (factory, _) = Throwing(new InvalidOperationException(SecretDetail), environment: "Development");

        var response = await WithToken(factory.CreateClient(), UserRole.PlatformAdmin).PostAsJsonAsync("/api/clinics", new { name = "Clinic" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var (_, raw) = await ReadProblemAsync(response);
        AssertNoInternals(raw);
    }

    // Logged as an error with the exception and the trace id the client got, so
    // support can find it - but without the request body, token or query.
    [Fact]
    public async Task UnexpectedException_IsLoggedWithTraceId_ButNotTheRequestOrToken()
    {
        var (factory, logs) = Throwing(new InvalidOperationException("boom"));
        var token = TestJwt.CreateValidToken(UserRole.PlatformAdmin);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/clinics?note=query-secret", new { name = "body-secret-name" });
        var (body, _) = await ReadProblemAsync(response);
        var traceId = body.GetProperty("traceId").GetString()!;

        var error = Assert.Single(logs.Entries, e => e.Level == LogLevel.Error && e.Category == typeof(ApiExceptionHandler).FullName);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains("POST /api/clinics", error.Message);

        Assert.Contains(traceId, error.Message);

        var allLogs = string.Join("\n", logs.Entries.Select(e => e.Message));
        Assert.DoesNotContain("body-secret-name", allLogs);
        Assert.DoesNotContain("query-secret", allLogs);
        Assert.DoesNotContain(token, allLogs);
    }

    [Fact]
    public void Map_CoversEveryApplicationException()
    {
        var inner = new Exception();

        Assert.Equal(409, ApiExceptionHandler.Map(new DuplicateReferralException("x", inner)).Status);
        Assert.Equal(409, ApiExceptionHandler.Map(new DuplicateRewardRedemptionException("x", inner)).Status);
        Assert.Equal(409, ApiExceptionHandler.Map(new DuplicateServiceOccurrenceException("x", inner)).Status);
        Assert.Equal(409, ApiExceptionHandler.Map(new ReferralAlreadyValidatedException("x", inner)).Status);
        Assert.Equal(500, ApiExceptionHandler.Map(new Exception("x")).Status);
    }

    private sealed class ThrowingClinicRepository(Exception exception) : IClinicRepository
    {
        public Task AddAsync(Clinic clinic, LoyaltyConfiguration loyaltyConfiguration, CancellationToken cancellationToken = default) =>
            throw exception;

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) => throw exception;
    }
}
