namespace DentalLoyalty.Infrastructure.Security;

// Stable, explicit claim names for tokens issued by JwtTokenGenerator.
// JwtBearerOptions.MapInboundClaims is disabled at registration so these
// names round-trip unchanged into HttpContext.User - the future
// tenant-context implementation must read exactly these.
public static class JwtClaimNames
{
    public const string UserId = "userId";

    public const string Role = "role";

    public const string ClinicId = "clinicId";
}
