using System.Diagnostics;
using System.IO;

namespace PanelTracker.App.Services;

/// <summary>Открытие файлов и папок средствами Windows.</summary>
public static class ShellLauncher
{
    public static void OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Открывает проводник с выделенным файлом.</summary>
    public static void RevealInExplorer(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        else
        {
            throw new FileNotFoundException("Файл или папка не найдены.", path);
        }
    }
}
