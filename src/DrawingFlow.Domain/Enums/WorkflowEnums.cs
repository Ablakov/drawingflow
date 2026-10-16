namespace DrawingFlow.Domain.Enums;

public enum ReviewDecision
{
    Approved = 0,
    RevisionRequired = 1,
}

public enum WorkflowTaskType
{
    /// <summary>Проверить версию документа.</summary>
    Review = 0,
    /// <summary>Доработать документ и загрузить новую версию.</summary>
    Rework = 1,
}

public enum WorkflowTaskStatus
{
    Open = 0,
    Completed = 1,
    Cancelled = 2,
}
