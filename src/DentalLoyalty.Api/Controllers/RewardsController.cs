using System.ComponentModel.DataAnnotations;
using DentalLoyalty.Application.Rewards;
using DentalLoyalty.Application.Rewards.Commands;
using DentalLoyalty.Application.Rewards.Queries;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// The clinic's reward definitions (BUSINESS.md §10, §18 "manage reward
// definitions", §21, §22). Rewards are created only here - no clinic is seeded
// with default rewards.
//
// ClinicAdmin only. No request carries a clinic id: every read and write is of
// the caller's own clinic, resolved from the authenticated tenant
// (ICurrentTenant), so a reward of another clinic is indistinguishable from
// none (404). PlatformAdmin is not granted these, as for services and loyalty
// configuration: it has no clinic of its own.
//
// This manages definitions only. A patient's reward states and redemptions are
// GET /api/patients/{id}/rewards and POST .../redemptions, whose rules are
// unchanged. Rewards are never deleted - redemptions refer to them;
// deactivation withdraws a reward (it can no longer be redeemed), activation
// offers it again.
[ApiController]
[Route("api/rewards")]
[Authorize(Roles = nameof(UserRole.ClinicAdmin))]
public class RewardsController : ControllerBase
{
    private readonly GetRewardsHandler _getRewardsHandler;
    private readonly GetRewardByIdHandler _getRewardByIdHandler;
    private readonly CreateRewardHandler _createRewardHandler;
    private readonly UpdateRewardHandler _updateRewardHandler;
    private readonly SetRewardActiveHandler _setRewardActiveHandler;

    public RewardsController(
        GetRewardsHandler getRewardsHandler,
        GetRewardByIdHandler getRewardByIdHandler,
        CreateRewardHandler createRewardHandler,
        UpdateRewardHandler updateRewardHandler,
        SetRewardActiveHandler setRewardActiveHandler)
    {
        _getRewardsHandler = getRewardsHandler;
        _getRewardByIdHandler = getRewardByIdHandler;
        _createRewardHandler = createRewardHandler;
        _updateRewardHandler = updateRewardHandler;
        _setRewardActiveHandler = setRewardActiveHandler;
    }

    // isActive filters by status; omitted, active and inactive rewards are listed.
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RewardDefinitionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var rewards = await _getRewardsHandler.HandleAsync(new GetRewardsQuery(isActive), cancellationToken);

        return rewards is null ? Forbid() : Ok(rewards);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RewardDefinitionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var reward = await _getRewardByIdHandler.HandleAsync(new GetRewardByIdQuery(id), cancellationToken);

        return reward is null ? NotFound() : Ok(reward);
    }

    [HttpPost]
    [ProducesResponseType<RewardDefinitionDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        RewardRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createRewardHandler.HandleAsync(
            new CreateRewardCommand(
                request.Name,
                request.Description,
                request.RequiredPoints!.Value,
                request.RequiredReferrals!.Value),
            cancellationToken);

        return result.Status == RewardCommandStatus.Saved
            ? CreatedAtAction(nameof(GetById), new { id = result.Reward!.Id }, result.Reward)
            : Rejected(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        RewardRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _updateRewardHandler.HandleAsync(
            new UpdateRewardCommand(
                id,
                request.Name,
                request.Description,
                request.RequiredPoints!.Value,
                request.RequiredReferrals!.Value),
            cancellationToken);

        return result.Status == RewardCommandStatus.Saved ? NoContent() : Rejected(result);
    }

    [HttpPost("{id:guid}/deactivation")]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, isActive: false, cancellationToken);

    [HttpPost("{id:guid}/activation")]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, isActive: true, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var found = await _setRewardActiveHandler.HandleAsync(
            new SetRewardActiveCommand(id, isActive),
            cancellationToken);

        return found ? NoContent() : NotFound();
    }

    private IActionResult Rejected(RewardCommandResult result)
    {
        switch (result.Status)
        {
            case RewardCommandStatus.Invalid:
                var (field, message) = result.InvalidField switch
                {
                    RewardField.Name => (nameof(RewardRequest.Name), $"The name is required and can be at most {Reward.MaximumNameLength} characters."),
                    RewardField.Description => (nameof(RewardRequest.Description), $"The description can be at most {Reward.MaximumDescriptionLength} characters."),
                    RewardField.RequiredPoints => (nameof(RewardRequest.RequiredPoints), $"Required points must be between 0 and {LoyaltyAccount.MaximumPointsBalance}."),
                    RewardField.RequiredReferrals => (nameof(RewardRequest.RequiredReferrals), "Required referrals cannot be negative."),
                    _ => throw new InvalidOperationException($"Unexpected field {result.InvalidField}."),
                };

                ModelState.AddModelError(field, message);
                return ValidationProblem(ModelState);

            case RewardCommandStatus.NotFound:
                return NotFound();

            case RewardCommandStatus.NoTenant:
                return Forbid();

            default:
                throw new InvalidOperationException($"Unexpected result {result.Status}.");
        }
    }
}

// RequiredPoints and RequiredReferrals are nullable only so that a missing value
// is rejected (400) instead of silently binding to 0 - a valid requirement that
// would make the reward immediately available.
public sealed record RewardRequest(
    string Name,
    string? Description,
    [Required] int? RequiredPoints,
    [Required] int? RequiredReferrals);
