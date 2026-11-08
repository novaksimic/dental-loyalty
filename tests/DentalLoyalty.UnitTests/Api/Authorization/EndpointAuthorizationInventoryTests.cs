using DentalLoyalty.UnitTests.Api.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DentalLoyalty.UnitTests.Api.Authorization;

// The API's role matrix (SECURITY.md §5.4), pinned against the endpoints the
// running app actually exposes. ASP.NET Core endpoints are anonymous unless
// annotated, so this fails if:
//   - a new endpoint is added without being consciously placed in this matrix,
//   - an endpoint loses its [Authorize]/role restriction or gains [AllowAnonymous],
//   - an endpoint's permitted roles change.
// Behavioural 401/403 tests for each endpoint live in the other test classes.
public class EndpointAuthorizationInventoryTests : IClassFixture<DentalLoyaltyApiFactory>
{
    private const string Anonymous = "<anonymous>";

    private static readonly Dictionary<string, string> ExpectedPolicy = new()
    {
        ["POST api/auth/login"] = Anonymous,
        ["POST api/clinics"] = "PlatformAdmin",
        ["POST api/patients"] = "ClinicAdmin,PlatformAdmin",
        ["GET api/patients/{id:guid}"] = "ClinicAdmin",
        ["PUT api/patients/{id:guid}"] = "ClinicAdmin",
        ["DELETE api/patients/{id:guid}"] = "ClinicAdmin",
        ["GET api/patients/me"] = "Patient",
        ["POST api/patients/{id:guid}/services"] = "ClinicAdmin",
        ["GET api/patients/{id:guid}/rewards"] = "ClinicAdmin",
        ["POST api/patients/{id:guid}/rewards/{rewardid:guid}/redemptions"] = "ClinicAdmin",
        ["GET api/patients/me/loyalty-history"] = "Patient",
        ["GET api/patients/me/loyalty-history/transactions"] = "Patient",
        ["GET api/patients/{id:guid}/loyalty-history"] = "ClinicAdmin",
        ["GET api/patients/{id:guid}/loyalty-history/transactions"] = "ClinicAdmin",
        ["POST api/referrals"] = "ClinicAdmin",
        ["POST api/referrals/{id:guid}/validation"] = "ClinicAdmin",
        ["GET api/loyalty-configuration"] = "ClinicAdmin",
        ["PUT api/loyalty-configuration"] = "ClinicAdmin",
    };

    private readonly DentalLoyaltyApiFactory _factory;

    public EndpointAuthorizationInventoryTests(DentalLoyaltyApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void EveryExposedEndpoint_HasExactlyTheExpectedAuthorizationPolicy()
    {
        // Ensure the host (and therefore its endpoint routing) is built.
        _factory.CreateClient();

        var endpoints = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        var actualPolicy = new Dictionary<string, string>();

        foreach (var endpoint in endpoints)
        {
            var method = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Single();
            var key = $"{method} {endpoint.RoutePattern.RawText!.ToLowerInvariant()}";

            actualPolicy[key] = DescribePolicy(endpoint);
        }

        Assert.Equal(
            ExpectedPolicy.OrderBy(p => p.Key),
            actualPolicy.OrderBy(p => p.Key));
    }

    private static string DescribePolicy(Endpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return Anonymous;
        }

        var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        // Authenticated-only (no roles) is not an accepted state for this API:
        // every protected endpoint must name the roles it permits.
        var roleRestrictions = authorizeData
            .Where(a => !string.IsNullOrWhiteSpace(a.Roles))
            .ToList();

        if (authorizeData.Count == 0 || roleRestrictions.Count != 1)
        {
            return $"<unexpected: {authorizeData.Count} [Authorize], {roleRestrictions.Count} with roles>";
        }

        return string.Join(
            ",",
            roleRestrictions[0].Roles!
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Order(StringComparer.Ordinal));
    }
}
