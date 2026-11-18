using DentalLoyalty.Api.Bootstrap;
using DentalLoyalty.Api.Configuration;
using DentalLoyalty.Api.Security;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Infrastructure;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// ICurrentTenant/ICurrentUser are implemented here (not in Infrastructure)
// because resolving them requires HttpContext, which only the API layer should
// touch (Application stays free of ASP.NET Core/HTTP concerns).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, CurrentTenant>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddControllers();

// ProblemDetails errors, the exception handler, CORS, the login rate limit and
// trusted forwarded headers - all from validated configuration.
builder.Services.AddApiHardening(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT bearer token. Example: \"Bearer eyJhbGciOi...\"",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document),
            new List<string>()
        },
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

// Fail fast on invalid Jwt, Cors, RateLimiting or ForwardedHeaders settings,
// before anything else runs.
app.ValidateApiConfiguration();

// Middleware order matters (SECURITY.md §10.7, §16, §17):
//  1. Forwarded headers first, so everything after sees the real client address
//     and scheme - and only when trusted proxies are configured.
//  2. The exception handler and status code pages wrap everything below, so
//     every error leaves as ProblemDetails.
//  3. CORS after routing and before authentication, so preflight requests are
//     answered without a token.
//  4. The rate limiter after routing (its policy is chosen per endpoint) and
//     before authentication, so rejected logins do no further work.
if (app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ForwardedHeadersSettings>>().Value.IsEnabled)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseCors(CorsSettings.PolicyName);

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await PlatformAdminBootstrapper.RunAsync(app);

app.Run();

// Exposes the generated top-level Program class to WebApplicationFactory<Program>
// in the test project (which otherwise cannot see the compiler's internal one).
public partial class Program
{
}