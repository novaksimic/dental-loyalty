using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// The clinic's scoring configuration (BUSINESS.md §3.1, §18; SECURITY.md §5.2/§5.4
// "Loyalty Configuration" = Own Clinic). ClinicAdmin only: neither endpoint takes
// a clinic id - the configuration is always the caller's own clinic's, resolved
// from the authenticated tenant (ICurrentTenant), so there is nothing a caller
// can supply to reach another clinic's configuration.
//
// PlatformAdmin is not granted these: it provisions the default configuration
// when it creates a clinic (CreateClinicHandler, SECURITY.md §5.1) and has no
// other configuration-management use case. Patients may not change loyalty
// configuration (SECURITY.md §5.3).
[ApiController]
[Route("api/loyalty-configuration")]
[Authorize(Roles = nameof(UserRole.ClinicAdmin))]
public class LoyaltyConfigurationController : ControllerBase
{
    private readonly GetLoyaltyConfigurationHandler _getLoyaltyConfigurationHandler;
    private readonly UpdateLoyaltyConfigurationHandler _updateLoyaltyConfigurationHandler;

    public LoyaltyConfigurationController(
        GetLoyaltyConfigurationHandler getLoyaltyConfigurationHandler,
        UpdateLoyaltyConfigurationHandler updateLoyaltyConfigurationHandler)
    {
        _getLoyaltyConfigurationHandler = getLoyaltyConfigurationHandler;
        _updateLoyaltyConfigurationHandler = updateLoyaltyConfigurationHandler;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var configuration = await _getLoyaltyConfigurationHandler.HandleAsync(
            new GetLoyaltyConfigurationQuery(),
            cancellationToken);

        if (configuration is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(configuration));
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        UpdateLoyaltyConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        bool updated;

        try
        {
            updated = await _updateLoyaltyConfigurationHandler.HandleAsync(
                new UpdateLoyaltyConfigurationCommand(request.SpendAmountRsd, request.PointsAwarded),
                cancellationToken);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // Rejected by LoyaltyConfiguration's validation before anything was saved.
            ModelState.AddModelError(ex.ParamName ?? string.Empty, "The value is not a valid scoring configuration value.");
            return ValidationProblem(ModelState);
        }

        return updated ? NoContent() : NotFound();
    }

    private static object ToResponse(LoyaltyConfiguration configuration) => new
    {
        configuration.ClinicId,
        configuration.SpendAmountRsd,
        configuration.PointsAwarded,
    };
}

public sealed record UpdateLoyaltyConfigurationRequest(decimal SpendAmountRsd, int PointsAwarded);
