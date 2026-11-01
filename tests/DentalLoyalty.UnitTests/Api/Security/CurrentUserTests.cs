using System.Security.Claims;
using DentalLoyalty.Api.Security;
using DentalLoyalty.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DentalLoyalty.UnitTests.Api.Security;

public class CurrentUserTests
{
    private static CurrentUser Build(HttpContext? httpContext)
    {
        return new CurrentUser(new HttpContextAccessor { HttpContext = httpContext });
    }

    private static HttpContext AuthenticatedContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void AuthenticatedUser_WithUserIdClaim_HasUser()
    {
        var userId = Guid.NewGuid();
        var context = AuthenticatedContext(
            new Claim(JwtClaimNames.UserId, userId.ToString()),
            new Claim(JwtClaimNames.Role, "Patient"),
            new Claim(JwtClaimNames.ClinicId, Guid.NewGuid().ToString()));

        var currentUser = Build(context);

        Assert.True(currentUser.HasUser);
        Assert.Equal(userId, currentUser.UserId);
    }

    [Fact]
    public void UnauthenticatedRequest_HasNoUser()
    {
        // No authenticationType => ClaimsIdentity.IsAuthenticated is false.
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(JwtClaimNames.UserId, Guid.NewGuid().ToString()),
        });
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var currentUser = Build(context);

        Assert.False(currentUser.HasUser);
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void NoHttpContext_HasNoUser()
    {
        var currentUser = Build(httpContext: null);

        Assert.False(currentUser.HasUser);
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void MissingUserIdClaim_HasNoUser()
    {
        var context = AuthenticatedContext(new Claim(JwtClaimNames.Role, "Patient"));

        var currentUser = Build(context);

        Assert.False(currentUser.HasUser);
        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void MalformedUserIdClaim_IsTreatedAsNoUser_DoesNotThrow()
    {
        var context = AuthenticatedContext(
            new Claim(JwtClaimNames.UserId, "not-a-guid"),
            new Claim(JwtClaimNames.Role, "Patient"));

        var currentUser = Build(context);

        Assert.False(currentUser.HasUser);
        Assert.Null(currentUser.UserId);
    }
}
