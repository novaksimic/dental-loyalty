using System.Net;
using System.Net.Http.Json;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.UnitTests.Api.TestSupport;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// POST /api/auth/login is how a token is obtained in the first place, so it
// must never require one. A bad-credentials request without a token should
// reach the handler (401 from LoginHandler) rather than being rejected by the
// authentication middleware itself.
public class LoginEndpointRemainsAnonymousTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private readonly DentalLoyaltyApiFactory _factory;

    public LoginEndpointRemainsAnonymousTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_WithoutToken_IsReachable_AndRejectsUnknownCredentials()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginCommand("nobody@example.com", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
