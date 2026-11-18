using DentalLoyalty.Application.Clinics.Commands;
using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Identity;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// Clinic creation is a platform-level operation (SECURITY.md §5.1, §5.4 RBAC matrix:
// "Manage Clinics" = PlatformAdmin only).
//
// POST {clinicId}/admins provisions a ClinicAdmin account for an existing clinic
// (ADR-010 decision 1): PlatformAdmin only, like the rest of this controller.
// ClinicAdmin accounts are never self-registered. The clinic comes only from the
// route - the body carries no clinic - and the response never carries the
// password or its hash.
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.PlatformAdmin))]
public class ClinicsController : ControllerBase
{
    private readonly CreateClinicHandler _createClinicHandler;
    private readonly CreateClinicAdminHandler _createClinicAdminHandler;

    public ClinicsController(
        CreateClinicHandler createClinicHandler,
        CreateClinicAdminHandler createClinicAdminHandler)
    {
        _createClinicHandler = createClinicHandler;
        _createClinicAdminHandler = createClinicAdminHandler;
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

    [HttpPost("{clinicId:guid}/admins")]
    public async Task<IActionResult> CreateAdmin(
        Guid clinicId,
        CreateClinicAdminRequest request,
        CancellationToken cancellationToken)
    {
        CreateClinicAdminResult result;

        try
        {
            result = await _createClinicAdminHandler.HandleAsync(
                new CreateClinicAdminCommand(clinicId, request.Email, request.Password),
                cancellationToken);
        }
        catch (DuplicateEmailException)
        {
            return EmailAlreadyExists();
        }

        switch (result.Status)
        {
            case CreateClinicAdminStatus.Created:
                return StatusCode(StatusCodes.Status201Created, new
                {
                    id = result.UserId,
                    clinicId,
                    email = result.Email,
                    role = nameof(UserRole.ClinicAdmin),
                });

            // Field-level errors, so a form can show them next to the input. The
            // submitted values are never echoed back.
            case CreateClinicAdminStatus.InvalidEmail:
                ModelState.AddModelError(nameof(request.Email), "The email is not a valid email address.");
                return ValidationProblem(ModelState);

            case CreateClinicAdminStatus.InvalidPassword:
                ModelState.AddModelError(
                    nameof(request.Password),
                    $"The password must be {PasswordPolicy.MinimumLength}-{PasswordPolicy.MaximumLength} characters long and must not be the email address.");
                return ValidationProblem(ModelState);

            case CreateClinicAdminStatus.ClinicNotFound:
                return NotFound();

            case CreateClinicAdminStatus.EmailAlreadyExists:
                return EmailAlreadyExists();

            default:
                throw new InvalidOperationException($"Unexpected result {result.Status}.");
        }
    }

    private ObjectResult EmailAlreadyExists() =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "A user with this email already exists.");
}

public sealed record CreateClinicAdminRequest(string Email, string Password);
