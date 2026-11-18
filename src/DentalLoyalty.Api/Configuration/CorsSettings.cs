using System.Net;
using Microsoft.Extensions.Options;

namespace DentalLoyalty.Api.Configuration;

// The browser origins allowed to call the API cross-origin - the Angular Admin
// application (SECURITY.md §17). Configuration only: no origin is hardcoded.
//
//   "Cors": { "AllowedOrigins": [ "https://admin.example.com" ] }
//
// Empty (the appsettings.json default) allows no cross-origin calls, which is
// right when the frontend is served from the API's own origin.
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public const string PolicyName = "AdminApp";

    public string[] AllowedOrigins { get; set; } = [];

    // Origins in the form the CORS middleware compares: scheme://host[:port],
    // lower-cased, no trailing slash. Valid only after validation.
    public string[] NormalizedOrigins() =>
        AllowedOrigins.Select(o => new Uri(o.Trim()).GetLeftPart(UriPartial.Authority).ToLowerInvariant()).Distinct().ToArray();
}

// Each origin must be an exact scheme://host[:port]. No wildcards ("*" or
// "*.example.com"), paths, queries or credentials. https is required except for
// loopback hosts (localhost, 127.0.0.1, ::1), which the Angular dev server uses.
// Values are echoed in errors: origins are not secrets.
public sealed class CorsSettingsValidator : IValidateOptions<CorsSettings>
{
    public ValidateOptionsResult Validate(string? name, CorsSettings settings)
    {
        var failures = new List<string>();

        foreach (var origin in settings.AllowedOrigins ?? [])
        {
            var value = origin?.Trim() ?? string.Empty;

            if (value.Contains('*'))
            {
                failures.Add($"Cors:AllowedOrigins: wildcard origins are not allowed ('{value}').");
                continue;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || uri.AbsolutePath != "/"
                || uri.Query.Length > 0
                || uri.Fragment.Length > 0
                || uri.UserInfo.Length > 0
                || value.EndsWith('/'))
            {
                failures.Add($"Cors:AllowedOrigins: '{value}' is not an origin of the form https://host[:port].");
                continue;
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !IsLoopback(uri))
            {
                failures.Add($"Cors:AllowedOrigins: '{value}' must use https (http is allowed only for localhost).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
}
