namespace DentalLoyalty.Application.Common.Paging;

// One page of a list. Page is 1-based; TotalCount counts every matching item,
// not just this page; TotalPages is 0 when nothing matches. A page beyond the
// last has no items but the same totals.
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(items, page, pageSize, totalCount, totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize));
}
