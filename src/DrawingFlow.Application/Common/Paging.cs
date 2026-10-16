using DrawingFlow.Contracts;

namespace DrawingFlow.Application.Common;

public static class Paging
{
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 50;
        }

        if (pageSize > PageRequest.MaxPageSize)
        {
            pageSize = PageRequest.MaxPageSize;
        }

        return (page, pageSize);
    }

    /// <summary>Приводит дату из запроса к UTC (Npgsql требует Kind = Utc для timestamptz).</summary>
    public static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };
}
