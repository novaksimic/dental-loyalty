using System.Security.Claims;
using DentalLoyalty.Api.Security;
using DentalLoyalty.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DentalLoyalty.UnitTests.Api.Security;

public class CurrentTenantTests
{
    private static CurrentTenant Build(HttpContext? httpContext)
    {
        return new CurrentTenant(new HttpContextAccessor { HttpContext = httpContext });
    }

    private static HttpContext AuthenticatedContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void AuthenticatedClinicAdmin_WithClinicIdClaim_HasTenant()
    {
        var clinicId = Guid.NewGuid();
        var context = AuthenticatedContext(
            new Claim(JwtClaimNames.UserId, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Role, "ClinicAdmin"),
            new Claim(JwtClaimNames.ClinicId, clinicId.ToString()));

        var currentTenant = Build(context);

        Assert.True(currentTenant.HasTenant);
        Assert.Equal(clinicId, currentTenant.ClinicId);
    }

    [Fact]
    public void AuthenticatedPlatformAdmin_WithoutClinicIdClaim_HasNoTenant()
    {
        var context = AuthenticatedContext(
            new Claim(JwtClaimNames.UserId, Guid.NewGuid().ToString()),
            new Claim(JwtClaimNames.Role, "PlatformAdmin"));

        var currentTenant = Build(context);

        Assert.False(currentTenant.HasTenant);
        Assert.Null(currentTenant.ClinicId);
    }

    [Fact]
    public void UnauthenticatedRequest_HasNoTenant()
    {
        // No authenticationType => ClaimsIdentity.IsAuthenticated is false, the
        // same shape an anonymous request's HttpContext.User has.
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.ClinicId, Guid.NewGuid().ToString()),
        });
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var currentTenant = Build(context);

        Assert.False(currentTenant.HasTenant);
        Assert.Null(currentTenant.ClinicId);
    }

    [Fact]
    public void NoHttpContext_HasNoTenant()
    {
        // Simulates resolution outside an HTTP request (e.g. a background scope).
        var currentTenant = Build(httpContext: null);

        Assert.False(currentTenant.HasTenant);
        Assert.Null(currentTenant.ClinicId);
    }

    [Fact]
    public void MalformedClinicIdClaim_IsTreatedAsNoTenant_DoesNotThrow()
    {
        var context = AuthenticatedContext(
            new Claim(JwtClaimNames.Role, "ClinicAdmin"),
            new Claim(JwtClaimNames.ClinicId, "not-a-guid"));

        var currentTenant = Build(context);

        Assert.False(currentTenant.HasTenant);
        Assert.Null(currentTenant.ClinicId);
    }
}
