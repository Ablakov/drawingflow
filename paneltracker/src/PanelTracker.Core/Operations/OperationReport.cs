namespace PanelTracker.Core.Operations;

/// <summary>Итог массовой операции над панелями.</summary>
public sealed class OperationReport
{
    private readonly List<string> _moved = [];
    private readonly List<string> _skipped = [];
    private readonly List<OperationError> _errors = [];

    /// <summary>Панели, файлы которых перемещены.</summary>
    public IReadOnlyList<string> Moved => _moved;

    /// <summary>Панели, уже находившиеся в целевом статусе.</summary>
    public IReadOnlyList<string> Skipped => _skipped;

    public IReadOnlyList<OperationError> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    internal void AddMoved(string panelId) => _moved.Add(panelId);

    internal void AddSkipped(string panelId) => _skipped.Add(panelId);

    internal void AddError(string panelId, string message) => _errors.Add(new OperationError(panelId, message));
}

public sealed record OperationError(string PanelId, string Message);
