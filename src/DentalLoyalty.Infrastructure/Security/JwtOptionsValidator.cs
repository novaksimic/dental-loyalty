using System.Text;
using Microsoft.Extensions.Options;

namespace DentalLoyalty.Infrastructure.Security;

// Validates the Jwt configuration section when the application starts
// (SECURITY.md §9.4). A missing or weak value stops the application with a clear
// error instead of failing on the first login or authenticated request.
//
// Failure messages name the configuration key only - never its value - so the
// signing key cannot leak through a startup error or log.
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    // HMAC-SHA256 needs a key of at least 256 bits.
    public const int MinimumSigningKeyBytes = 32;

    public const int MaximumAccessTokenLifetimeMinutes = 24 * 60;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add(
                $"{JwtOptions.SectionName}:SigningKey is required. Supply it through user secrets (Development) " +
                "or an environment variable / secret store (other environments); never commit it.");
        }
        else if (Encoding.UTF8.GetByteCount(options.SigningKey) < MinimumSigningKeyBytes)
        {
            failures.Add(
                $"{JwtOptions.SectionName}:SigningKey must be at least {MinimumSigningKeyBytes} bytes " +
                $"({MinimumSigningKeyBytes * 8} bits) for HMAC-SHA256.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"{JwtOptions.SectionName}:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"{JwtOptions.SectionName}:Audience is required.");
        }

        if (options.AccessTokenLifetimeMinutes < 1 || options.AccessTokenLifetimeMinutes > MaximumAccessTokenLifetimeMinutes)
        {
            failures.Add(
                $"{JwtOptions.SectionName}:AccessTokenLifetimeMinutes must be between 1 and {MaximumAccessTokenLifetimeMinutes}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
