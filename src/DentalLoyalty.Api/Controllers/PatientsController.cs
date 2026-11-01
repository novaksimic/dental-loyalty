using DentalLoyalty.Application.Common.Exceptions;
using DentalLoyalty.Application.Loyalty.Commands;
using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Application.Patients.Commands;
using DentalLoyalty.Application.Patients.Queries;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// Patient management is clinic-level administration (SECURITY.md §5.4 RBAC matrix:
// "Manage Patients" = PlatformAdmin platform-wide, ClinicAdmin own clinic).
//
// Create is shared by PlatformAdmin/ClinicAdmin: ClinicId is still client-supplied
// in the request body, but CreatePatientHandler (Slice 5) only honors it for
// PlatformAdmin - a ClinicAdmin's own tenant (ICurrentTenant) is always
// authoritative regardless of what the body contains.
//
// Read/update/deactivate are ClinicAdmin-only and tenant-scoped via ICurrentTenant:
// the resource lookup itself is filtered by the caller's own ClinicId, so a
// patient belonging to another clinic is indistinguishable from one that doesn't
// exist (both 404). PlatformAdmin is deliberately NOT granted these - SECURITY.md
// §5.1/§7.12: platform-level privileges must not automatically imply unrestricted
// access to patient data, and no existing use case requires it.
//
// POST {id}/services records a completed service for a patient of the
// ClinicAdmin's own clinic and credits the points it earns (BUSINESS.md §3, §5.1;
// SECURITY.md §5.4 "Manage Points" = Own Clinic). PlatformAdmin is not granted it,
// for the same reason as the other id-based patient endpoints. The body carries no
// clinic, price or points: they come from the tenant, the catalog and the clinic's
// scoring configuration.
//
// GET {id}/rewards and POST {id}/rewards/{rewardId}/redemptions are the Clinic
// Admin's view of the patient's rewards and confirmation that the patient used one
// (BUSINESS.md §18, §19; SECURITY.md §5.4 "Redeem Reward" = Own Clinic), scoped
// to the caller's clinic like the other id-based patient endpoints. Eligibility is
// always evaluated by the backend; the request carries no balances.
//
// GET /me is Patient self-service (SECURITY.md §5.4 "Own Profile"): it takes no
// id at all - the patient is resolved from the authenticated UserId (ICurrentUser)
// within the authenticated tenant (ICurrentTenant), so there is nothing a caller
// can tamper with to reach another patient's record.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PatientsController : ControllerBase
{
    private readonly CreatePatientHandler _createPatientHandler;
    private readonly GetPatientByIdHandler _getPatientByIdHandler;
    private readonly GetCurrentPatientHandler _getCurrentPatientHandler;
    private readonly UpdatePatientContactHandler _updatePatientContactHandler;
    private readonly DeactivatePatientHandler _deactivatePatientHandler;
    private readonly RecordPatientServiceHandler _recordPatientServiceHandler;
    private readonly GetPatientRewardsHandler _getPatientRewardsHandler;
    private readonly RedeemRewardHandler _redeemRewardHandler;

    public PatientsController(
        CreatePatientHandler createPatientHandler,
        GetPatientByIdHandler getPatientByIdHandler,
        GetCurrentPatientHandler getCurrentPatientHandler,
        UpdatePatientContactHandler updatePatientContactHandler,
        DeactivatePatientHandler deactivatePatientHandler,
        RecordPatientServiceHandler recordPatientServiceHandler,
        GetPatientRewardsHandler getPatientRewardsHandler,
        RedeemRewardHandler redeemRewardHandler)
    {
        _createPatientHandler = createPatientHandler;
        _getPatientByIdHandler = getPatientByIdHandler;
        _getCurrentPatientHandler = getCurrentPatientHandler;
        _updatePatientContactHandler = updatePatientContactHandler;
        _deactivatePatientHandler = deactivatePatientHandler;
        _recordPatientServiceHandler = recordPatientServiceHandler;
        _getPatientRewardsHandler = getPatientRewardsHandler;
        _redeemRewardHandler = redeemRewardHandler;
    }

    [HttpPost]
    [Authorize(Roles = $"{nameof(UserRole.PlatformAdmin)},{nameof(UserRole.ClinicAdmin)}")]
    public async Task<IActionResult> Create(
        CreatePatientCommand command,
        CancellationToken cancellationToken)
    {
        var patientId = await _createPatientHandler.HandleAsync(
            command,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = patientId },
            new { id = patientId });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var patient = await _getPatientByIdHandler.HandleAsync(
            new GetPatientByIdQuery(id),
            cancellationToken);

        if (patient is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(patient));
    }

    [HttpGet("me")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var patient = await _getCurrentPatientHandler.HandleAsync(
            new GetCurrentPatientQuery(),
            cancellationToken);

        if (patient is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(patient));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public async Task<IActionResult> UpdateContact(
        Guid id,
        UpdatePatientContactRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _updatePatientContactHandler.HandleAsync(
            new UpdatePatientContactCommand(id, request.PhoneNumber, request.Email),
            cancellationToken);

        return updated ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var deactivated = await _deactivatePatientHandler.HandleAsync(
            new DeactivatePatientCommand(id),
            cancellationToken);

        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/services")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public async Task<IActionResult> RecordService(
        Guid id,
        RecordPatientServiceRequest request,
        CancellationToken cancellationToken)
    {
        RecordPatientServiceResult result;

        try
        {
            result = await _recordPatientServiceHandler.HandleAsync(
                new RecordPatientServiceCommand(id, request.Id, request.ServiceId, request.PerformedAt?.UtcDateTime),
                cancellationToken);
        }
        catch (DuplicateServiceOccurrenceException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This service occurrence has already been recorded.");
        }
        catch (ConcurrencyConflictException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient's loyalty account or cycle was changed by another operation. Nothing was saved.");
        }

        return result.Status switch
        {
            RecordPatientServiceStatus.RecordedWithPoints or RecordPatientServiceStatus.RecordedWithoutPoints =>
                StatusCode(StatusCodes.Status201Created, new
                {
                    id = result.PatientServiceId,
                    pointsAwarded = result.Status == RecordPatientServiceStatus.RecordedWithPoints,
                    earnedPoints = result.EarnedPoints,
                    creditedPoints = result.CreditedPoints,
                    cycleCompleted = result.CycleCompleted,
                }),
            RecordPatientServiceStatus.InvalidRequest =>
                ValidationProblem(detail: "The service occurrence id is required and the performance time must not be in the future."),
            RecordPatientServiceStatus.PatientNotFound or RecordPatientServiceStatus.ServiceNotFound =>
                NotFound(),
            RecordPatientServiceStatus.PatientInactive =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient is inactive."),
            RecordPatientServiceStatus.ServiceInactive =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The catalog service is inactive."),
            RecordPatientServiceStatus.AlreadyRecorded =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "This service occurrence has already been recorded."),
            _ => throw new InvalidOperationException($"Unexpected result {result.Status}."),
        };
    }

    [HttpGet("{id:guid}/rewards")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public async Task<IActionResult> GetRewards(
        Guid id,
        CancellationToken cancellationToken)
    {
        var rewards = await _getPatientRewardsHandler.HandleAsync(
            new GetPatientRewardsQuery(id),
            cancellationToken);

        if (rewards is null)
        {
            return NotFound();
        }

        return Ok(new
        {
            points = rewards.Points,
            referralBalance = rewards.ReferralBalance,
            rewards = rewards.Rewards.Select(r => new
            {
                id = r.RewardId,
                r.Name,
                r.Description,
                r.RequiredPoints,
                r.RequiredReferrals,
                status = r.Status.ToString(),
            }),
        });
    }

    [HttpPost("{id:guid}/rewards/{rewardId:guid}/redemptions")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public async Task<IActionResult> RedeemReward(
        Guid id,
        Guid rewardId,
        CancellationToken cancellationToken)
    {
        RedeemRewardResult result;

        try
        {
            result = await _redeemRewardHandler.HandleAsync(
                new RedeemRewardCommand(id, rewardId),
                cancellationToken);
        }
        catch (DuplicateRewardRedemptionException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This reward has already been redeemed in the patient's loyalty cycle.");
        }
        catch (ConcurrencyConflictException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient's loyalty account or cycle was changed by another operation. Nothing was saved.");
        }

        return result.Status switch
        {
            RedeemRewardStatus.Redeemed =>
                StatusCode(StatusCodes.Status201Created, new
                {
                    id = result.RedemptionId,
                    rewardId,
                    redeemedAt = result.RedeemedAt,
                    cycleCompleted = result.CycleCompleted,
                }),
            RedeemRewardStatus.PatientNotFound or RedeemRewardStatus.RewardNotFound =>
                NotFound(),
            RedeemRewardStatus.PatientInactive =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient is inactive."),
            RedeemRewardStatus.RewardUnavailable =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The reward is not offered by the clinic."),
            RedeemRewardStatus.NotEligible =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "The patient has not unlocked this reward."),
            RedeemRewardStatus.AlreadyRedeemed =>
                Problem(statusCode: StatusCodes.Status409Conflict, title: "This reward has already been redeemed in the patient's loyalty cycle."),
            _ => throw new InvalidOperationException($"Unexpected result {result.Status}."),
        };
    }

    private static object ToResponse(Patient patient) => new
    {
        patient.Id,
        patient.ClinicId,
        patient.FirstName,
        patient.LastName,
        patient.PhoneNumber,
        patient.Email,
        patient.IsActive,
    };
}

public sealed record UpdatePatientContactRequest(string PhoneNumber, string? Email);

// Id is the caller-chosen id of this service occurrence: resubmitting it never
// records or credits the service twice. PerformedAt defaults to now.
public sealed record RecordPatientServiceRequest(Guid Id, Guid ServiceId, DateTimeOffset? PerformedAt);
