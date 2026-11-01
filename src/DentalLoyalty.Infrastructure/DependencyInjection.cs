using System.Text;
using DentalLoyalty.Application.Clinics.Commands;
using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Application.Loyalty;
using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.Application.Patients.Queries;
using DentalLoyalty.Application.Referrals.Commands;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using DentalLoyalty.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DentalLoyalty.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DentalLoyaltyDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"));
        });

        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<CreatePatientHandler>();
        services.AddScoped<GetPatientByIdHandler>();
        services.AddScoped<GetCurrentPatientHandler>();
        services.AddScoped<UpdatePatientContactHandler>();
        services.AddScoped<DeactivatePatientHandler>();
        services.AddScoped<IClinicRepository, ClinicRepository>();
        services.AddScoped<CreateClinicHandler>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ILoyaltyConfigurationRepository, LoyaltyConfigurationRepository>();
        services.AddScoped<ILoyaltyAccountRepository, LoyaltyAccountRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<ILoyaltyCycleRepository, LoyaltyCycleRepository>();
        services.AddScoped<LoyaltyCycleProgression>();
        services.AddScoped<IPatientServiceRepository, PatientServiceRepository>();
        services.AddScoped<RecordPatientServiceHandler>();
        services.AddScoped<IRewardRepository, RewardRepository>();
        services.AddScoped<GetPatientRewardsHandler>();
        services.AddScoped<RedeemRewardHandler>();
        services.AddScoped<IReferralRepository, ReferralRepository>();
        services.AddScoped<CreateReferralHandler>();
        services.AddScoped<ValidateReferralHandler>();
        services.AddScoped<GetLoyaltyConfigurationHandler>();
        services.AddScoped<UpdateLoyaltyConfigurationHandler>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<CreatePlatformAdminHandler>();
        services.AddScoped<CreateClinicAdminHandler>();
        services.AddScoped<CreatePatientUserHandler>();
        services.AddScoped<LoginHandler>();

        services.AddJwtAuthentication(configuration);

        return services;
    }

    private static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);

        services.Configure<JwtOptions>(jwtSection);
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Configured against IOptions<JwtOptions> (resolved lazily, when the
        // handler first needs it) rather than a JwtOptions snapshot captured
        // here - IConfiguration can still gain sources (e.g. test
        // configuration overrides) after this method runs but before the
        // service provider is built, and this keeps JwtBearerOptions in sync
        // with whatever IOptions<JwtOptions> ultimately resolves to.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
            {
                var jwtOptions = jwtOptionsAccessor.Value;

                // Keep issued claim names (JwtClaimNames) unchanged instead of
                // remapping "role"/"userId" to long .NET claim type URIs.
                bearerOptions.MapInboundClaims = false;

                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    // JwtTokenGenerator issues "role"/"userId" (JwtClaimNames), not the
                    // long ClaimTypes URIs ASP.NET Core's Roles-based [Authorize] and
                    // User.Identity.Name default to - map them explicitly so
                    // User.IsInRole()/[Authorize(Roles=...)] and HttpContext.User.Identity.Name
                    // work against the claims this API actually issues.
                    RoleClaimType = JwtClaimNames.Role,
                    NameClaimType = JwtClaimNames.UserId,
                };
            });

        services.AddAuthorization();

        return services;
    }
}