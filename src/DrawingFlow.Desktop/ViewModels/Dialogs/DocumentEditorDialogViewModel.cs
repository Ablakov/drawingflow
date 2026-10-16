using CommunityToolkit.Mvvm.ComponentModel;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Documents;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed partial class DocumentEditorDialogViewModel : DialogViewModelBase
{
    private readonly DrawingFlowApiClient _api;
    private readonly Guid _projectId;
    private readonly DocumentDto? _existing;

    public DocumentEditorDialogViewModel(DrawingFlowApiClient api, Guid projectId, DocumentDto? existing)
    {
        _api = api;
        _projectId = projectId;
        _existing = existing;
        _documentNumber = existing?.DocumentNumber ?? string.Empty;
        _documentTitle = existing?.Title ?? string.Empty;
        _description = existing?.Description ?? string.Empty;
        _discipline = existing?.Discipline ?? string.Empty;
        _documentType = existing?.DocumentType ?? string.Empty;
    }

    public override string Title => IsEdit ? "Изменение сведений о документе" : "Регистрация документа";

    public override string ConfirmText => IsEdit ? "Сохранить" : "Зарегистрировать";

    public override double Width => 560;

    public bool IsEdit => _existing is not null;

    public bool IsCreate => !IsEdit;

    public DocumentDto? Result { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _documentNumber;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _documentTitle;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _discipline;

    [ObservableProperty]
    private string _documentType;

    protected override bool CanConfirm() => base.CanConfirm() && !string.IsNullOrWhiteSpace(DocumentNumber) && !string.IsNullOrWhiteSpace(DocumentTitle);

    protected override Task<bool> OnConfirmAsync() => RunAsync(async () =>
    {
        static string? Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        Result = IsEdit
            ? await _api.UpdateDocumentAsync(_existing!.Id, new UpdateDocumentRequest(DocumentTitle.Trim(), Clean(Description), Clean(Discipline), Clean(DocumentType), _existing.RowVersion))
            : await _api.RegisterDocumentAsync(_projectId, new RegisterDocumentRequest(DocumentNumber.Trim(), DocumentTitle.Trim(), Clean(Description), Clean(Discipline), Clean(DocumentType)));
    });
}
