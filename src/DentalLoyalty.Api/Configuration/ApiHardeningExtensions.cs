using System.Net;
using System.Threading.RateLimiting;
using DentalLoyalty.Api.Errors;
using DentalLoyalty.Infrastructure.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DentalLoyalty.Api.Configuration;

// Cross-cutting API protection: ProblemDetails error responses, the exception
// handler, CORS for the Admin application, the login rate limit and trusted
// forwarded headers. Every setting comes from configuration and is validated at
// startup (SECURITY.md §10.7, §16, §17).
public static class ApiHardeningExtensions
{
    // Only what the Admin application uses: bearer-token JSON requests. No
    // credentials (cookies) are allowed - tokens travel in the Authorization
    // header - so a credentialed request from another origin is never honoured.
    private static readonly string[] CorsMethods = ["GET", "POST", "PUT", "DELETE"];

    private static readonly string[] CorsRequestHeaders = ["Authorization", "Content-Type"];

    // Location (201 responses) and Retry-After (429) are readable by the app.
    private static readonly string[] CorsExposedHeaders = ["Location", "Retry-After"];

    public static IServiceCollection AddApiHardening(this IServiceCollection services, IConfiguration configuration)
    {
        // --- Errors: every error status is answered with ProblemDetails. -----
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();

        // --- CORS -----------------------------------------------------------
        services.AddOptions<CorsSettings>()
            .Bind(configuration.GetSection(CorsSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CorsSettings>, CorsSettingsValidator>();

        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<CorsSettings>>((cors, settings) =>
                cors.AddPolicy(CorsSettings.PolicyName, policy => policy
                    .WithOrigins(settings.Value.NormalizedOrigins())
                    .WithMethods(CorsMethods)
                    .WithHeaders(CorsRequestHeaders)
                    .WithExposedHeaders(CorsExposedHeaders)
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

        // --- Login rate limit -------------------------------------------------
        services.AddOptions<LoginRateLimitSettings>()
            .Bind(configuration.GetSection(LoginRateLimitSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LoginRateLimitSettings>, LoginRateLimitSettingsValidator>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRateLimitRejectionAsync;

            // Applied only where an endpoint opts in ([EnableRateLimiting]) -
            // today only POST /api/auth/login. There is no global limiter.
            options.AddPolicy(LoginRateLimitSettings.PolicyName, httpContext =>
            {
                var settings = httpContext.RequestServices
                    .GetRequiredService<IOptions<LoginRateLimitSettings>>().Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientAddress(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        // --- Forwarded headers (only from configured proxies) -----------------
        services.AddOptions<ForwardedHeadersSettings>()
            .Bind(configuration.GetSection(ForwardedHeadersSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ForwardedHeadersSettings>, ForwardedHeadersSettingsValidator>();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // Only the address added by the nearest (trusted) proxy is used.
                options.ForwardLimit = 1;

                // Nothing is trusted by default - not even loopback.
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();

                foreach (var proxy in settings.Value.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in settings.Value.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        return services;
    }

    // Resolves every validated setting once, so an invalid configuration stops
    // the application before it does anything else (including the PlatformAdmin
    // bootstrap's database access) - with OptionsValidationException naming the
    // offending keys. ValidateOnStart checks them again when the host starts.
    public static void ValidateApiConfiguration(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        _ = app.Services.GetRequiredService<IOptions<CorsSettings>>().Value;
        _ = app.Services.GetRequiredService<IOptions<LoginRateLimitSettings>>().Value;
        _ = app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;
    }

    // The client's address as the connection reports it - or, when forwarded
    // headers are enabled, as the trusted proxy reported it (UseForwardedHeaders
    // runs first). IPv4-mapped IPv6 addresses count as their IPv4 address.
    private static string ClientAddress(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress;

        if (address is null)
        {
            return "unknown";
        }

        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    // The same response for every rejected attempt, whatever the email: the
    // limiter runs before the login handler, so the response cannot reveal
    // whether an account exists.
    private static async ValueTask WriteRateLimitRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DentalLoyalty.Api.RateLimiting")
            .LogWarning(
                "Rate limit exceeded for {Method} {Path} from {ClientAddress}.",
                httpContext.Request.Method,
                httpContext.Request.Path.Value,
                ClientAddress(httpContext));

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests. Try again later.",
            },
        });
    }
}
