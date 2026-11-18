namespace DentalLoyalty.UnitTests.Api.TestSupport;

// The browser origins the API test host is configured with (Cors:AllowedOrigins).
internal static class TestOrigins
{
    public const string Allowed = "https://admin.test.example";

    public const string NotAllowed = "https://evil.test.example";
}
