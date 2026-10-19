using PanelTracker.Core;
using PanelTracker.Core.History;
using PanelTracker.Core.Models;

namespace PanelTracker.Core.Tests;

public class PanelOperationsTests
{
    private static TrackerSession Open(TempProject project) => new(project.Root, project.Settings, "tester", "PC1");

    [Fact]
    public void MoveToStatus_physically_moves_file_and_logs_history()
    {
        using var project = new TempProject();
        var source = project.AddFile("ready_to_check", "N021_R2.pdf");
        var session = Open(project);
        var scan = session.Refresh();

        var report = session.MoveToStatus(scan.Panels, "sent");

        Assert.Equal(new[] { "N021" }, report.Moved);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(project.FolderOf("sent"), "N021_R2.pdf")));
        var entry = Assert.Single(session.History.ReadAll());
        Assert.Equal(HistoryAction.Moved, entry.Action);
        Assert.Equal("tester", entry.User);
        Assert.Equal("PC1", entry.Machine);
        Assert.Equal("ready_to_check", entry.FromStatus);
        Assert.Equal("sent", entry.ToStatus);
        Assert.Equal("sent", session.Refresh().FindPanel("N021")!.StatusKey);
    }

    [Fact]
    public void Moving_many_panels_reports_skipped_and_conflicts()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N1.pdf");
        project.AddFile("sent", "N2.pdf");
        project.AddFile("ready_to_check", "N3.pdf");
        project.AddFile("sent", "N3.pdf"); // конфликт: в целевой папке уже есть такой файл
        var session = Open(project);
        var scan = session.Refresh();

        var report = session.MoveToStatus(scan.Panels, "sent");

        Assert.Equal(new[] { "N1" }, report.Moved);
        Assert.Equal(new[] { "N2", "N3" }.OrderBy(x => x), report.Skipped.OrderBy(x => x));
        Assert.Empty(report.Errors);
        Assert.True(File.Exists(Path.Combine(project.FolderOf("ready_to_check"), "N3.pdf")));
    }

    [Fact]
    public void Conflict_in_target_folder_is_an_error_not_an_overwrite()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N3.pdf", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        project.AddFile("problem", "N3.pdf", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var session = Open(project);
        var scan = session.Refresh();
        Assert.Equal("ready_to_check", scan.FindPanel("N3")!.StatusKey);

        var report = session.MoveToStatus(scan.Panels, "problem");

        var error = Assert.Single(report.Errors);
        Assert.Equal("N3", error.PanelId);
        Assert.Contains("уже есть файл", error.Message);
        Assert.True(File.Exists(Path.Combine(project.FolderOf("ready_to_check"), "N3.pdf")));
        Assert.Empty(session.History.ReadAll());
    }

    [Fact]
    public void Unknown_status_key_is_rejected()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N1.pdf");
        var session = Open(project);
        var scan = session.Refresh();

        Assert.Throws<ArgumentException>(() => session.MoveToStatus(scan.Panels, "nope"));
    }

    [Fact]
    public void Hold_and_release_return_panel_to_previous_status()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_send", "N1.pdf");
        project.AddFile("ready_to_check", "N2.pdf");
        var session = Open(project);

        var held = session.Hold(session.Refresh().Panels);
        Assert.Equal(2, held.Moved.Count);
        Assert.All(session.Refresh().Panels, p => Assert.Equal("hold", p.StatusKey));

        var released = session.Release(session.Refresh().Panels);

        Assert.Equal(2, released.Moved.Count);
        var scan = session.Refresh();
        Assert.Equal("ready_to_send", scan.FindPanel("N1")!.StatusKey);
        Assert.Equal("ready_to_check", scan.FindPanel("N2")!.StatusKey);
        Assert.Equal(
            new[] { HistoryAction.Held, HistoryAction.Held, HistoryAction.Released, HistoryAction.Released },
            session.History.ReadAll().Select(e => e.Action));
    }

    [Fact]
    public void Release_without_history_falls_back_to_first_status()
    {
        using var project = new TempProject();
        project.AddFile("hold", "N1.pdf");
        project.AddFile("sent", "N2.pdf");
        var session = Open(project);

        var report = session.Release(session.Refresh().Panels);

        Assert.Equal(new[] { "N1" }, report.Moved);
        Assert.Equal(new[] { "N2" }, report.Skipped);
        Assert.Equal("ready_to_check", session.Refresh().FindPanel("N1")!.StatusKey);
    }

    [Fact]
    public void MarkProblem_moves_file_and_keeps_comment()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N1.pdf");
        var session = Open(project);

        var report = session.MarkProblem(session.Refresh().Panels, "Нет узла примыкания");
        Assert.Equal(new[] { "N1" }, report.Moved);
        Assert.Equal("problem", session.Refresh().FindPanel("N1")!.StatusKey);
        Assert.Equal("Нет узла примыкания", session.History.LatestComment("N1", HistoryAction.ProblemMarked));

        var again = session.MarkProblem(session.Refresh().Panels, "Уточнено: ждём заказчика");
        Assert.Equal(new[] { "N1" }, again.Skipped);
        Assert.Equal("Уточнено: ждём заказчика", session.History.LatestComment("N1", HistoryAction.ProblemMarked));
    }

    [Fact]
    public void Missing_target_folder_is_created()
    {
        using var project = new TempProject(createStatusFolders: false);
        Directory.CreateDirectory(project.FolderOf("ready_to_check"));
        project.AddFile("ready_to_check", "N1.pdf");
        var session = Open(project);

        var report = session.MoveToStatus(session.Refresh().Panels, "sent");

        Assert.Equal(new[] { "N1" }, report.Moved);
        Assert.True(File.Exists(Path.Combine(project.FolderOf("sent"), "N1.pdf")));
    }

    [Fact]
    public void EnsureStatusFolders_creates_missing_folders_only()
    {
        using var project = new TempProject(createStatusFolders: false);
        Directory.CreateDirectory(project.FolderOf("sent"));
        var session = Open(project);

        var created = session.EnsureStatusFolders();

        Assert.Equal(project.Settings.Statuses.Count - 1, created.Count);
        Assert.Empty(session.Refresh().MissingFolders);
        Assert.Empty(session.EnsureStatusFolders());
    }
}
