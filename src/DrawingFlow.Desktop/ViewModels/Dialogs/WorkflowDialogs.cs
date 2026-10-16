using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Workflow;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed partial class ReviewerChoice(ReviewerCandidateDto candidate) : ObservableObject
{
    public ReviewerCandidateDto Candidate { get; } = candidate;

    public string Label => $"{Candidate.DisplayName} ({Candidate.Login}) — {Names.Of(Candidate.Role)}";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Отправка текущей версии на проверку: выбор проверяющих, срок, поручение.</summary>
public sealed partial class SubmitForReviewDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly DocumentDto _document;

    public SubmitForReviewDialogViewModel(DrawingFlowApiClient api, DocumentDto document, IReadOnlyList<ReviewerCandidateDto> candidates)
    {
        _api = api;
        _document = document;
        foreach (var candidate in candidates)
        {
            var choice = new ReviewerChoice(candidate);
            choice.PropertyChanged += (_, _) => ConfirmCommand.NotifyCanExecuteChanged();
            Reviewers.Add(choice);
        }
    }

    public override string Title => $"Отправить на проверку: {_document.DocumentNumber}, версия {_document.CurrentVersion?.VersionNumber}";

    public override string ConfirmText => "Отправить";

    public override double Width => 560;

    public ObservableCollection<ReviewerChoice> Reviewers { get; } = [];

    public bool HasCandidates => Reviewers.Count > 0;

    [ObservableProperty]
    private DateTime? _dueDate;

    [ObservableProperty]
    private string _comment = string.Empty;

    protected override bool CanConfirm() => base.CanConfirm() && Reviewers.Any(r => r.IsSelected);

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        var ids = Reviewers.Where(r => r.IsSelected).Select(r => r.Candidate.UserId).ToList();
        DateTime? due = DueDate is DateTime d ? DateTime.SpecifyKind(d.Date.AddHours(18), DateTimeKind.Local).ToUniversalTime() : null;
        await _api.SubmitForReviewAsync(_document.Id, new SubmitForReviewRequest(_document.CurrentVersion!.Id, ids, due, string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim()));
    });
}

/// <summary>Решение проверяющего: согласовать или вернуть на доработку (комментарий обязателен).</summary>
public sealed partial class ReviewDecisionDialogViewModel(DrawingFlowApiClient api, Guid documentId, Guid versionId, string documentLabel, ReviewDecision decision) : DialogViewModelBase
{
    public override string Title => decision == ReviewDecision.Approved ? "Согласование версии" : "Возврат на доработку";

    public override string ConfirmText => decision == ReviewDecision.Approved ? "Согласовать" : "Вернуть на доработку";

    public override double Width => 540;

    public string DocumentLabel => documentLabel;

    public bool IsRevision => decision == ReviewDecision.RevisionRequired;

    public string CommentLabel => IsRevision ? "Замечания (обязательно)" : "Комментарий (необязательно)";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _comment = string.Empty;

    protected override bool CanConfirm() => base.CanConfirm() && (!IsRevision || !string.IsNullOrWhiteSpace(Comment));

    protected override Task<bool> OnConfirmAsync() => RunAsync(() =>
        api.DecideReviewAsync(documentId, new ReviewDecisionRequest(versionId, decision, string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim())));
}
