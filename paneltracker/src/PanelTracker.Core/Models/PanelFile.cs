namespace PanelTracker.Core.Models;

/// <summary>Один файл чертежа, найденный в папке статуса.</summary>
public sealed record PanelFile(string PanelId, int? Revision, string FullPath, string StatusKey, DateTime LastWriteTimeUtc)
{
    public string FileName => Path.GetFileName(FullPath);

    public string Directory => Path.GetDirectoryName(FullPath) ?? string.Empty;

    public string RevisionLabel => Revision is { } r ? $"R{r}" : string.Empty;
}
