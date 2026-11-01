using DentalLoyalty.Application.Clinics.Commands;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// Clinic creation is a platform-level operation (SECURITY.md §5.1, §5.4 RBAC matrix:
// "Manage Clinics" = PlatformAdmin only).
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.PlatformAdmin))]
public class ClinicsController : ControllerBase
{
    private readonly CreateClinicHandler _createClinicHandler;

    public ClinicsController(
        CreateClinicHandler createClinicHandler)
    {
        _createClinicHandler = createClinicHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateClinicCommand command,
        CancellationToken cancellationToken)
    {
        var clinicId = await _createClinicHandler.HandleAsync(
            command,
            cancellationToken);

        return CreatedAtAction(
            nameof(Create),
            new { id = clinicId },
            new { id = clinicId });
    }
}