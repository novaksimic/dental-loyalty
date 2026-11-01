using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Application.Identity.Commands;

namespace DentalLoyalty.Api.Bootstrap;

// SETUP/DEVELOPMENT INFRASTRUCTURE ONLY.
//
// Creates the initial PlatformAdmin account. This is NOT an HTTP endpoint
// and is never reachable by clients - there is deliberately no public
// registration flow for PlatformAdmin.
//
// Runs once at startup, and only when both Bootstrap:PlatformAdmin:Email
// and Bootstrap:PlatformAdmin:Password are supplied via configuration
// (environment variables or user-secrets - never committed appsettings
// values). Omit that configuration to disable it; leaving it wired in is
// otherwise safe, since it no-ops whenever a User with the configured
// email already exists.
public static class PlatformAdminBootstrapper
{
    public static async Task RunAsync(
        WebApplication app,
        CancellationToken cancellationToken = default)
    {
        var email = app.Configuration["Bootstrap:PlatformAdmin:Email"];
        var password = app.Configuration["Bootstrap:PlatformAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        using var scope = app.Services.CreateScope();

        var userRepository = scope.ServiceProvider
            .GetRequiredService<IUserRepository>();

        var existingUser = await userRepository.GetByEmailAsync(
            email,
            cancellationToken);

        if (existingUser is not null)
        {
            return;
        }

        var handler = scope.ServiceProvider
            .GetRequiredService<CreatePlatformAdminHandler>();

        await handler.HandleAsync(
            new CreatePlatformAdminCommand(email, password),
            cancellationToken);

        // Never log the password - only confirm that the account was created.
        app.Logger.LogInformation(
            "Bootstrap PlatformAdmin account created for {Email}.",
            email);
    }
}
