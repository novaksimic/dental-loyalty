using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Loyalty.Queries;

public sealed record GetLoyaltyTransactionsQuery(
    LoyaltyHistorySubject Subject,
    int Page = GetLoyaltyTransactionsQuery.DefaultPage,
    int PageSize = GetLoyaltyTransactionsQuery.DefaultPageSize)
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 20;

    // BACKEND & API ARCHITECTURE.md §40: no unreasonable page sizes.
    public const int MaximumPageSize = 100;
}

public enum GetLoyaltyTransactionsStatus
{
    Found = 1,
    InvalidPaging = 2,
    PatientNotFound = 3,
}

public sealed record LoyaltyTransactionsPage(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<LoyaltyTransactionRecord> Items);

public sealed record GetLoyaltyTransactionsResult(
    GetLoyaltyTransactionsStatus Status,
    LoyaltyTransactionsPage? Transactions = null);

// One page of the patient's loyalty transactions (BUSINESS.md §14, §18), newest
// first: every balance change - service and referral points, redemptions (no
// change, §13), manual adjustments - and the reset recorded when a cycle ended
// (§5.4).
//
// Shown as of now (ADR-008 decision 6): the CycleReset of a cycle that has
// expired but whose expiry the next operation has yet to record is included,
// at the cycle's end - after everything recorded in that cycle - exactly as it
// will be recorded. Reading never saves anything.
public sealed class GetLoyaltyTransactionsHandler
{
    private readonly LoyaltyHistoryStateLoader _stateLoader;
    private readonly ILoyaltyHistoryRepository _historyRepository;

    public GetLoyaltyTransactionsHandler(
        LoyaltyHistoryStateLoader stateLoader,
        ILoyaltyHistoryRepository historyRepository)
    {
        _stateLoader = stateLoader;
        _historyRepository = historyRepository;
    }

    public async Task<GetLoyaltyTransactionsResult> HandleAsync(
        GetLoyaltyTransactionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > GetLoyaltyTransactionsQuery.MaximumPageSize)
        {
            return new GetLoyaltyTransactionsResult(GetLoyaltyTransactionsStatus.InvalidPaging);
        }

        var state = await _stateLoader.LoadAsync(query.Subject, cancellationToken);

        if (state is null)
        {
            return new GetLoyaltyTransactionsResult(GetLoyaltyTransactionsStatus.PatientNotFound);
        }

        // Pending resets are later than every recorded transaction, so they come
        // first in newest-first order; PendingTransitions is oldest first.
        var pending = state.PendingTransitions
            .Reverse()
            .Select(t => new LoyaltyTransactionRecord(
                t.Reset.CreatedAt,
                LoyaltyTransactionType.CycleReset,
                t.Reset.PointsChange,
                t.Reset.ReferralChange,
                ServiceName: null,
                RewardName: null,
                IsReferrer: null,
                t.Ended.Status))
            .ToList();

        var recordedCount = await _historyRepository.CountTransactionsAsync(
            state.PatientId,
            state.ClinicId,
            cancellationToken);

        var total = recordedCount + pending.Count;
        var skip = (long)(query.Page - 1) * query.PageSize;
        var items = new List<LoyaltyTransactionRecord>(Math.Min(query.PageSize, Math.Max(total, 0)));

        if (skip < total)
        {
            items.AddRange(pending.Skip((int)Math.Min(skip, pending.Count)).Take(query.PageSize));

            var remaining = query.PageSize - items.Count;

            if (remaining > 0)
            {
                var recordedSkip = (int)Math.Max(0, skip - pending.Count);

                items.AddRange(await _historyRepository.ListTransactionsAsync(
                    state.PatientId,
                    state.ClinicId,
                    recordedSkip,
                    remaining,
                    cancellationToken));
            }
        }

        return new GetLoyaltyTransactionsResult(
            GetLoyaltyTransactionsStatus.Found,
            new LoyaltyTransactionsPage(
                query.Page,
                query.PageSize,
                total,
                items.Select(i => i with { OccurredAt = LoyaltyHistoryStateLoader.AsUtc(i.OccurredAt) }).ToList()));
    }
}
