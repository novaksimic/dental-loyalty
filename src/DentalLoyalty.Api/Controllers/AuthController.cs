using DentalLoyalty.Api.Configuration;
using DentalLoyalty.Application.Identity.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DentalLoyalty.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly LoginHandler _loginHandler;

    public AuthController(LoginHandler loginHandler)
    {
        _loginHandler = loginHandler;
    }

    // Must stay reachable without a token - this is how a token is obtained.
    // Rate-limited per client IP (LoginRateLimitSettings): over the limit, 429
    // before any credential is checked, so it reveals nothing about accounts.
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitSettings.PolicyName)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _loginHandler.HandleAsync(
            command,
            cancellationToken);

        if (result is null)
        {
            return Unauthorized();
        }

        return Ok(result);
    }
}
