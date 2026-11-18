using Microsoft.Extensions.Options;

namespace DentalLoyalty.Api.Configuration;

// The login rate limit (SECURITY.md §16): at most PermitLimit attempts per
// client IP address in each fixed window of WindowSeconds; further attempts in
// the window are rejected with 429 without being processed.
//
//   "RateLimiting": { "Login": { "PermitLimit": 10, "WindowSeconds": 60 } }
public sealed class LoginRateLimitSettings
{
    public const string SectionName = "RateLimiting:Login";

    public const string PolicyName = "login";

    public int PermitLimit { get; set; } = 10;

    public int WindowSeconds { get; set; } = 60;
}

public sealed class LoginRateLimitSettingsValidator : IValidateOptions<LoginRateLimitSettings>
{
    public ValidateOptionsResult Validate(string? name, LoginRateLimitSettings settings)
    {
        var failures = new List<string>();

        if (settings.PermitLimit is < 1 or > 10_000)
        {
            failures.Add($"{LoginRateLimitSettings.SectionName}:PermitLimit must be between 1 and 10000.");
        }

        if (settings.WindowSeconds is < 1 or > 3_600)
        {
            failures.Add($"{LoginRateLimitSettings.SectionName}:WindowSeconds must be between 1 and 3600.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
