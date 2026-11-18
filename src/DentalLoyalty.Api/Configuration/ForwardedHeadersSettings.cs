using System.Net;
using Microsoft.Extensions.Options;

namespace DentalLoyalty.Api.Configuration;

// The reverse proxies whose X-Forwarded-For / X-Forwarded-Proto headers are
// trusted (SECURITY.md §16). The login rate limit is per client IP; behind a
// proxy the connection's address is the proxy's, so the client's address must
// come from X-Forwarded-For - but only from a proxy listed here, or any client
// could pick its own address and escape the limit.
//
//   "ForwardedHeaders": { "KnownProxies": [ "10.0.0.5" ], "KnownNetworks": [ "10.0.0.0/24" ] }
//
// Both empty (the default): forwarded headers are ignored entirely and the
// connection's address is used.
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public string[] KnownProxies { get; set; } = [];

    public string[] KnownNetworks { get; set; } = [];

    public bool IsEnabled => KnownProxies.Length > 0 || KnownNetworks.Length > 0;
}

public sealed class ForwardedHeadersSettingsValidator : IValidateOptions<ForwardedHeadersSettings>
{
    public ValidateOptionsResult Validate(string? name, ForwardedHeadersSettings settings)
    {
        var failures = new List<string>();

        foreach (var proxy in settings.KnownProxies ?? [])
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                failures.Add($"ForwardedHeaders:KnownProxies: '{proxy}' is not an IP address.");
            }
        }

        foreach (var network in settings.KnownNetworks ?? [])
        {
            if (!IPNetwork.TryParse(network, out _))
            {
                failures.Add($"ForwardedHeaders:KnownNetworks: '{network}' is not a CIDR network (for example 10.0.0.0/24).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
