using DentalLoyalty.Application.Loyalty.Queries;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// A patient's loyalty history (BUSINESS.md §5.4, §14, §17, §18): the current
// state with each reward's status, every cycle with the rewards redeemed in it,
// the referrals the patient made, and - paged, newest first - every loyalty
// transaction. Read-only: nothing is written, including a pending cycle expiry
// (ADR-008 decision 6).
//
// GET me/... is Patient self-service (SECURITY.md §5.3 "own points, referrals,
// rewards, loyalty cycle information"): it takes no id - the patient is resolved
// from the authenticated UserId within the authenticated tenant, like GET
// /api/patients/me.
//
// GET {id}/... is the Clinic Admin's view of a patient of their own clinic
// (BUSINESS.md §18; SECURITY.md §5.4 "View Patient Data" = Own Clinic): a patient
// of another clinic is indistinguishable from none (404). PlatformAdmin is not
// granted it, as for the other id-based patient endpoints (SECURITY.md §7.12).
//
// Responses carry no internal ids: services, rewards and cycles are referred to
// by name, time and status, and the users who recorded each operation are not
// exposed.
[ApiController]
[Route("api/patients")]
[Authorize]
public class LoyaltyHistoryController : ControllerBase
{
    private readonly GetLoyaltyHistoryHandler _getLoyaltyHistoryHandler;
    private readonly GetLoyaltyTransactionsHandler _getLoyaltyTransactionsHandler;

    public LoyaltyHistoryController(
        GetLoyaltyHistoryHandler getLoyaltyHistoryHandler,
        GetLoyaltyTransactionsHandler getLoyaltyTransactionsHandler)
    {
        _getLoyaltyHistoryHandler = getLoyaltyHistoryHandler;
        _getLoyaltyTransactionsHandler = getLoyaltyTransactionsHandler;
    }

    [HttpGet("me/loyalty-history")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    public Task<IActionResult> GetMyHistory(CancellationToken cancellationToken) =>
        GetHistoryAsync(LoyaltyHistorySubject.CurrentPatient, cancellationToken);

    [HttpGet("me/loyalty-history/transactions")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    public Task<IActionResult> GetMyTransactions(
        [FromQuery] int page = GetLoyaltyTransactionsQuery.DefaultPage,
        [FromQuery] int pageSize = GetLoyaltyTransactionsQuery.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        GetTransactionsAsync(LoyaltyHistorySubject.CurrentPatient, page, pageSize, cancellationToken);

    [HttpGet("{id:guid}/loyalty-history")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public Task<IActionResult> GetPatientHistory(Guid id, CancellationToken cancellationToken) =>
        GetHistoryAsync(LoyaltyHistorySubject.Patient(id), cancellationToken);

    [HttpGet("{id:guid}/loyalty-history/transactions")]
    [Authorize(Roles = nameof(UserRole.ClinicAdmin))]
    public Task<IActionResult> GetPatientTransactions(
        Guid id,
        [FromQuery] int page = GetLoyaltyTransactionsQuery.DefaultPage,
        [FromQuery] int pageSize = GetLoyaltyTransactionsQuery.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        GetTransactionsAsync(LoyaltyHistorySubject.Patient(id), page, pageSize, cancellationToken);

    private async Task<IActionResult> GetHistoryAsync(
        LoyaltyHistorySubject subject,
        CancellationToken cancellationToken)
    {
        var history = await _getLoyaltyHistoryHandler.HandleAsync(
            new GetLoyaltyHistoryQuery(subject),
            cancellationToken);

        if (history is null)
        {
            return NotFound();
        }

        var status = history.Status;

        return Ok(new
        {
            status = new
            {
                points = status.Points,
                maximumPoints = status.MaximumPoints,
                referralBalance = status.ReferralBalance,
                maximumReferralRewards = status.MaximumReferralRewards,
                activeCycle = status.CycleStartedAt is null
                    ? null
                    : new { startedAt = status.CycleStartedAt, expiresAt = status.CycleExpiresAt },
                rewards = status.Rewards.Select(r => new
                {
                    name = r.Name,
                    description = r.Description,
                    requiredPoints = r.RequiredPoints,
                    requiredReferrals = r.RequiredReferrals,
                    status = r.Status.ToString(),
                }),
            },
            cycles = history.Cycles.Select(c => new
            {
                status = c.Status.ToString(),
                startedAt = c.StartedAt,
                endedAt = c.EndedAt,
                expiresAt = c.ExpiresAt,
                points = c.Points,
                referrals = c.Referrals,
                redemptions = c.Redemptions.Select(r => new
                {
                    rewardName = r.RewardName,
                    redeemedAt = r.RedeemedAt,
                }),
            }),
            referrals = history.Referrals.Select(r => new
            {
                status = r.Status.ToString(),
                referredAt = r.ReferredAt,
                examinedAt = r.ExaminedAt,
                rewardsForfeited = r.RewardsForfeited,
                rewarded = r.Rewarded,
                creditedPoints = r.CreditedPoints,
            }),
        });
    }

    private async Task<IActionResult> GetTransactionsAsync(
        LoyaltyHistorySubject subject,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _getLoyaltyTransactionsHandler.HandleAsync(
            new GetLoyaltyTransactionsQuery(subject, page, pageSize),
            cancellationToken);

        return result.Status switch
        {
            GetLoyaltyTransactionsStatus.Found => Ok(new
            {
                page = result.Transactions!.Page,
                pageSize = result.Transactions.PageSize,
                totalCount = result.Transactions.TotalCount,
                items = result.Transactions.Items.Select(t => new
                {
                    occurredAt = t.OccurredAt,
                    type = t.Type.ToString(),
                    pointsChange = t.PointsChange,
                    referralChange = t.ReferralChange,
                    serviceName = t.ServiceName,
                    rewardName = t.RewardName,
                    referralRole = t.IsReferrer switch
                    {
                        true => "Referrer",
                        false => "Referred",
                        null => null,
                    },
                    endedCycleStatus = t.EndedCycleStatus?.ToString(),
                }),
            }),
            GetLoyaltyTransactionsStatus.InvalidPaging =>
                ValidationProblem(detail: $"page must be at least 1 and pageSize between 1 and {GetLoyaltyTransactionsQuery.MaximumPageSize}."),
            GetLoyaltyTransactionsStatus.PatientNotFound =>
                NotFound(),
            _ => throw new InvalidOperationException($"Unexpected result {result.Status}."),
        };
    }
}
