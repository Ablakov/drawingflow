namespace DrawingFlow.Contracts;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

/// <summary>Параметры постраничного запроса. Значения ограничиваются сервером.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = 50)
{
    public const int MaxPageSize = 200;
}
