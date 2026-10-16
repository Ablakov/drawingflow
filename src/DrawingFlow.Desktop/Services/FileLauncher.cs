using System.Diagnostics;
using System.IO;

namespace DrawingFlow.Desktop.Services;

/// <summary>Открытие локальных файлов в связанных приложениях и показ в Проводнике.</summary>
public interface IFileLauncher
{
    void Open(string path);

    void RevealInFolder(string path);
}

public sealed class FileLauncher : IFileLauncher
{
    public void Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл не найден.", path);
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void RevealInFolder(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл не найден.", path);
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
