using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Identity;
using DentalLoyalty.Application.Identity.Commands;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// A patient's user account (ADR-010 decision 2). Patients do not self-register:
// a ClinicAdmin creates the account for a patient of their own clinic, and the
// link starts the patient's first loyalty cycle (BUSINESS.md §5.1).
//
// ClinicAdmin only, like the other id-based patient endpoints: the patient is
// loaded within the caller's tenant (ICurrentTenant), so a patient of another
// clinic is indistinguishable from none (404). The body carries no clinic.
// PlatformAdmin is not granted it - SECURITY.md §5.1/§7.12: platform-level
// privileges do not imply access to patient data.
//
// The response never carries the password or its hash, and submitted values are
// never echoed back in errors.
[ApiController]
[Route("api/patients/{id:guid}/account")]
[Authorize(Roles = nameof(UserRole.ClinicAdmin))]
public class PatientAccountsController : ControllerBase
{
    private readonly CreatePatientUserHandler _createPatientUserHandler;

    public PatientAccountsController(CreatePatientUserHandler createPatientUserHandler)
    {
        _createPatientUserHandler = createPatientUserHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid id,
        CreatePatientAccountRequest request,
        CancellationToken cancellationToken)
    {
        CreatePatientUserResult result;

        try
        {
            result = await _createPatientUserHandler.HandleAsync(
                new CreatePatientUserCommand(id, request.Email, request.Password),
                cancellationToken);
        }
        catch (DuplicateEmailException)
        {
            return EmailAlreadyExists();
        }
        catch (ConcurrencyConflictException)
        {
            return AlreadyLinked();
        }

        switch (result.Status)
        {
            case CreatePatientUserStatus.Created:
                return StatusCode(StatusCodes.Status201Created, new
                {
                    userId = result.UserId,
                    patientId = id,
                    email = result.Email,
                    role = nameof(UserRole.Patient),
                });

            case CreatePatientUserStatus.InvalidEmail:
                ModelState.AddModelError(nameof(request.Email), "The email is not a valid email address.");
                return ValidationProblem(ModelState);

            case CreatePatientUserStatus.InvalidPassword:
                ModelState.AddModelError(
                    nameof(request.Password),
                    $"The password must be {PasswordPolicy.MinimumLength}-{PasswordPolicy.MaximumLength} characters long and must not be the email address.");
                return ValidationProblem(ModelState);

            case CreatePatientUserStatus.PatientNotFound:
                return NotFound();

            case CreatePatientUserStatus.PatientInactive:
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient is inactive.");

            case CreatePatientUserStatus.AlreadyLinked:
                return AlreadyLinked();

            case CreatePatientUserStatus.EmailAlreadyExists:
                return EmailAlreadyExists();

            default:
                throw new InvalidOperationException($"Unexpected result {result.Status}.");
        }
    }

    private ObjectResult EmailAlreadyExists() =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "A user with this email already exists.");

    private ObjectResult AlreadyLinked() =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient already has a user account.");
}

public sealed record CreatePatientAccountRequest(string Email, string Password);
