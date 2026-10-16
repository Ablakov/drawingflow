using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.Domain.Workflow;

/// <summary>
/// Явные правила переходов статусов документа. Единственный источник истины для сервера и тестов.
/// Загрузка новой версии всегда возвращает документ в состояние «В работе»: согласование не наследуется.
/// </summary>
public static class DocumentWorkflow
{
    private static readonly IReadOnlyDictionary<DocumentStatus, DocumentStatus[]> Transitions = new Dictionary<DocumentStatus, DocumentStatus[]>
    {
        [DocumentStatus.Draft] = [DocumentStatus.InProgress, DocumentStatus.Archived],
        [DocumentStatus.InProgress] = [DocumentStatus.InReview, DocumentStatus.Archived],
        [DocumentStatus.InReview] = [DocumentStatus.Approved, DocumentStatus.RevisionRequired, DocumentStatus.InProgress],
        [DocumentStatus.RevisionRequired] = [DocumentStatus.InProgress, DocumentStatus.Archived],
        [DocumentStatus.Approved] = [DocumentStatus.Released, DocumentStatus.InProgress, DocumentStatus.Archived],
        [DocumentStatus.Released] = [DocumentStatus.InProgress, DocumentStatus.Archived],
        [DocumentStatus.Archived] = [DocumentStatus.Draft, DocumentStatus.InProgress],
    };

    public static bool CanTransition(DocumentStatus from, DocumentStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static void EnsureTransition(DocumentStatus from, DocumentStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainRuleException($"Переход статуса документа «{Name(from)}» → «{Name(to)}» недопустим.");
        }
    }

    /// <summary>Можно ли загружать новые версии в документ с данным статусом.</summary>
    public static bool AllowsNewVersion(DocumentStatus status) => status != DocumentStatus.Archived;

    public static string Name(DocumentStatus status) => status switch
    {
        DocumentStatus.Draft => "Черновик",
        DocumentStatus.InProgress => "В работе",
        DocumentStatus.InReview => "На проверке",
        DocumentStatus.RevisionRequired => "Требует доработки",
        DocumentStatus.Approved => "Согласован",
        DocumentStatus.Released => "Выпущен",
        DocumentStatus.Archived => "В архиве",
        _ => status.ToString(),
    };
}
