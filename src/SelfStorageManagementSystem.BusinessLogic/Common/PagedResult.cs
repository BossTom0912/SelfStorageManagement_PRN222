namespace SelfStorageManagementSystem.BusinessLogic.Common;

/// <summary>
/// Generic paginated result envelope containing items and pagination metadata.
/// Note: Pagination must be performed at database level using Skip/Take prior to materialization.
/// </summary>
/// <typeparam name="T">Item type</typeparam>
public class PagedResult<T>
{
    private readonly int _totalCount;
    private readonly int _pageNumber = 1;
    private readonly int _pageSize = 10;
    private readonly int? _totalPages;
    private readonly bool? _hasPreviousPage;
    private readonly bool? _hasNextPage;

    public IReadOnlyCollection<T> Items { get; init; } = Array.Empty<T>();

    public int PageNumber
    {
        get => _pageNumber;
        init => _pageNumber = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value < 1 ? 10 : value;
    }

    public int TotalCount
    {
        get => _totalCount;
        init => _totalCount = value < 0 ? 0 : value;
    }

    public int TotalPages
    {
        get => _totalPages ?? (PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0);
        init => _totalPages = value;
    }

    public bool HasPreviousPage
    {
        get => _hasPreviousPage ?? (PageNumber > 1 && TotalPages > 0 && PageNumber <= TotalPages);
        init => _hasPreviousPage = value;
    }

    public bool HasNextPage
    {
        get => _hasNextPage ?? (TotalPages > 0 && PageNumber < TotalPages);
        init => _hasNextPage = value;
    }

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
        HasPreviousPage = PageNumber > 1 && TotalPages > 0 && PageNumber <= TotalPages;
        HasNextPage = TotalPages > 0 && PageNumber < TotalPages;
    }
}
