using PanelTracker.Core;
using PanelTracker.Core.History;

namespace PanelTracker.Core.Tests;

public class TrackerSessionTests
{
    [Fact]
    public void External_moves_are_detected_and_logged_once()
    {
        using var project = new TempProject();
        var path = project.AddFile("ready_to_check", "N021.pdf");
        var session = new TrackerSession(project.Root, project.Settings, "tester");
        session.Refresh();

        // Пользователь перетащил файл в проводнике.
        File.Move(path, Path.Combine(project.FolderOf("sent"), "N021.pdf"));
        session.Refresh();
        session.Refresh();

        var entry = Assert.Single(session.History.ReadAll());
        Assert.Equal(HistoryAction.Detected, entry.Action);
        Assert.Equal(TrackerSession.ExternalUser, entry.User);
        Assert.Equal("ready_to_check", entry.FromStatus);
        Assert.Equal("sent", entry.ToStatus);
    }

    [Fact]
    public void Moves_made_by_the_program_are_not_logged_twice()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N021.pdf");
        var session = new TrackerSession(project.Root, project.Settings, "tester");

        session.MoveToStatus(session.Refresh().Panels, "sent");
        session.Refresh();

        var entry = Assert.Single(session.History.ReadAll());
        Assert.Equal(HistoryAction.Moved, entry.Action);
    }

    [Fact]
    public void First_scan_does_not_log_anything()
    {
        using var project = new TempProject();
        project.AddFile("sent", "N1.pdf");
        var session = new TrackerSession(project.Root, project.Settings, "tester");

        session.Refresh();

        Assert.Empty(session.History.ReadAll());
        Assert.Equal(1, session.Statistics().Sent);
    }
}
