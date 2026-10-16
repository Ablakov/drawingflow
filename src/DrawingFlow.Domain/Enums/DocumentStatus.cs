namespace DrawingFlow.Domain.Enums;

/// <summary>Статус документа (жизненный цикл согласования). Переходы — в <see cref="Workflow.DocumentWorkflow"/>.</summary>
public enum DocumentStatus
{
    Draft = 0,
    InProgress = 1,
    InReview = 2,
    RevisionRequired = 3,
    Approved = 4,
    Released = 5,
    Archived = 6,
}

/// <summary>Статус конкретной версии. Решения о согласовании всегда относятся к версии, а не к документу в целом.</summary>
public enum VersionStatus
{
    Registered = 0,
    InReview = 1,
    Approved = 2,
    RevisionRequired = 3,
    Released = 4,
    Superseded = 5,
}
