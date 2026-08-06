namespace IMS.Application.Common.Models;

/// <summary>Standard envelope for every list endpoint.</summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}

/// <summary>Common paging + sorting inputs accepted by list endpoints.</summary>
public class PagedQuery
{
    private const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = 25;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => 25,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    /// <summary>Free-text filter; each endpoint decides which columns it searches.</summary>
    public string? Search { get; set; }

    public int Skip => (Page - 1) * PageSize;
}
