using DrawingFlow.Contracts.Documents;
using DrawingFlow.Desktop.Localization;

namespace DrawingFlow.Desktop.ViewModels.Dialogs;

public sealed record CompareRow(string Field, string Left, string Right)
{
    public bool Differs => !string.Equals(Left, Right, StringComparison.Ordinal);
}

/// <summary>Сравнение метаданных двух версий (содержимое файлов не сравнивается).</summary>
public sealed class CompareVersionsDialogViewModel : DialogViewModelBase
{
    public CompareVersionsDialogViewModel(DocumentVersionDto left, DocumentVersionDto right)
    {
        LeftTitle = $"Версия {left.VersionNumber}";
        RightTitle = $"Версия {right.VersionNumber}";
        static string Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        Rows =
        [
            new CompareRow("Имя файла", left.OriginalFileName, right.OriginalFileName),
            new CompareRow("Размер", $"{Names.FormatSize(left.FileSize)} ({left.FileSize:N0} Б)", $"{Names.FormatSize(right.FileSize)} ({right.FileSize:N0} Б)"),
            new CompareRow("SHA-256", left.Sha256Hash, right.Sha256Hash),
            new CompareRow("Тип содержимого", left.ContentType, right.ContentType),
            new CompareRow("Автор", left.CreatedByName, right.CreatedByName),
            new CompareRow("Дата регистрации", Local(left.CreatedAtUtc), Local(right.CreatedAtUtc)),
            new CompareRow("Статус", Names.Of(left.Status), Names.Of(right.Status)),
            new CompareRow("Комментарий", left.Comment ?? "—", right.Comment ?? "—"),
        ];
        SameContent = string.Equals(left.Sha256Hash, right.Sha256Hash, StringComparison.OrdinalIgnoreCase);
    }

    public override string Title => "Сравнение версий";

    public override string ConfirmText => "Закрыть";

    public override bool ShowCancel => false;

    public override double Width => 760;

    public string LeftTitle { get; }

    public string RightTitle { get; }

    public IReadOnlyList<CompareRow> Rows { get; }

    public bool SameContent { get; }

    public string Summary => SameContent
        ? "Содержимое файлов идентично (контрольные суммы совпадают)."
        : "Содержимое файлов различается. Сравнение самих чертежей появится в следующих версиях системы.";

    protected override Task<bool> OnConfirmAsync() => Task.FromResult(true);
}
