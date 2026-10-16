namespace DrawingFlow.Contracts;

/// <summary>Тело ошибки в формате RFC 9457 (ProblemDetails) с расширениями DrawingFlow.</summary>
public sealed record ApiProblem
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? Instance { get; init; }
    /// <summary>Машиночитаемый код ошибки (например, validation, forbidden, conflict).</summary>
    public string? ErrorCode { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
