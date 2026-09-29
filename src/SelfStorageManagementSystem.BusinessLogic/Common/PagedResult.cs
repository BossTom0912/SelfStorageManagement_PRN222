namespace SelfStorageManagementSystem.BusinessLogic.Common;

/// <summary>
/// Generic paginated result envelope containing items and pagination metadata.
/// Note: Pagination must be performed at database level using Skip/Take prior to materialization.
/// </summary>
/// <typeparam name="T">Item type</typeparam>
public class PagedResult<T>
{
    public IReadOnlyCollection<T> Items { get; init; } = Array.Empty<T>();

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public int TotalPages { get; init; }

    public PagedResult()
    {
    }

    public PagedResult(IReadOnlyCollection<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items ?? Array.Empty<T>();
        TotalCount = totalCount < 0 ? 0 : totalCount;
        PageNumber = pageNumber < 1 ? 1 : pageNumber;
        PageSize = pageSize < 1 ? 10 : pageSize;
        TotalPages = (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
