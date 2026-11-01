using DentalLoyalty.Api.Bootstrap;
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

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