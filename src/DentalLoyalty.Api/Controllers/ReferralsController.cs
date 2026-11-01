using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Referrals.Commands;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// Referrals between patients of the ClinicAdmin's own clinic (BUSINESS.md §4;
// SECURITY.md §5.4 "Manage Referrals" = Own Clinic; ADR-007). POST records a
// Pending referral; POST {id}/validation confirms the referred patient's
// examination, which makes the referral valid and applies its rewards.
//
// Neither request carries a clinic, points or referral value: the clinic is the
// caller's tenant (ICurrentTenant), so a patient or referral of another clinic
// is indistinguishable from one that doesn't exist (404), and the rewards come
// from the business rules. PlatformAdmin is not granted these, for the same
// reason as the id-based patient endpoints (SECURITY.md §5.1/§7.12). Patient
// self-service referrals (SECURITY.md "Own only") are not implemented yet.
[ApiController]
[Route("api/referrals")]
[Authorize(Roles = nameof(UserRole.ClinicAdmin))]
public class ReferralsController : ControllerBase
{
    private readonly CreateReferralHandler _createReferralHandler;
    private readonly ValidateReferralHandler _validateReferralHandler;

    public ReferralsController(
        CreateReferralHandler createReferralHandler,
        ValidateReferralHandler validateReferralHandler)
    {
        _createReferralHandler = createReferralHandler;
        _validateReferralHandler = validateReferralHandler;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateReferralRequest request,
        CancellationToken cancellationToken)
    {
        CreateReferralResult result;

        try
        {
            result = await _createReferralHandler.HandleAsync(
                new CreateReferralCommand(request.ReferrerPatientId, request.ReferredPatientId),
                cancellationToken);
        }
        catch (DuplicateReferralException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "The referred patient has already been referred.");
        }

        return result.Status switch
        {
            CreateReferralStatus.Created =>
                StatusCode(StatusCodes.Status201Created, new
                {
                    id = result.ReferralId,
                    status = ReferralStatus.Pending.ToString(),
                }),
            CreateReferralStatus.SelfReferral =>
                ValidationProblem(detail: "A patient cannot refer themselves."),
            CreateReferralStatus.PatientNotFound =>
                NotFound(),
            CreateReferralStatus.PatientInactive =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient is inactive."),
            CreateReferralStatus.AlreadyReferred =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The referred patient has already been referred."),
            _ => throw new InvalidOperationException($"Unexpected result {result.Status}."),
        };
    }

    [HttpPost("{id:guid}/validation")]
    public async Task<IActionResult> Validate(
        Guid id,
        ValidateReferralRequest? request,
        CancellationToken cancellationToken)
    {
        ValidateReferralResult result;

        try
        {
            result = await _validateReferralHandler.HandleAsync(
                new ValidateReferralCommand(id, request?.ExaminedAt?.UtcDateTime),
                cancellationToken);
        }
        catch (ReferralAlreadyValidatedException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "The referral has already been validated.");
        }
        catch (ConcurrencyConflictException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "The referral or a patient's loyalty account or cycle was changed by another operation. Nothing was saved.");
        }

        return result.Status switch
        {
            ValidateReferralStatus.Validated =>
                Ok(new
                {
                    id,
                    status = ReferralStatus.Completed.ToString(),
                    referrer = ToResponse(result.Referrer!),
                    referred = ToResponse(result.Referred!),
                }),
            ValidateReferralStatus.InvalidRequest =>
                ValidationProblem(detail: "The examination time must not be in the future."),
            ValidateReferralStatus.ReferralNotFound =>
                NotFound(),
            ValidateReferralStatus.PatientInactive =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient is inactive."),
            ValidateReferralStatus.NotPending =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The referral has already been validated or was cancelled."),
            _ => throw new InvalidOperationException($"Unexpected result {result.Status}."),
        };
    }

    private static object ToResponse(ReferralRewardResult reward) => new
    {
        outcome = reward.Outcome.ToString(),
        creditedPoints = reward.CreditedPoints,
        cycleCompleted = reward.CycleCompleted,
    };
}

public sealed record CreateReferralRequest(Guid ReferrerPatientId, Guid ReferredPatientId);

// ExaminedAt defaults to now.
public sealed record ValidateReferralRequest(DateTimeOffset? ExaminedAt);
