using PanelTracker.Core.Models;
using PanelTracker.Core.Scanning;

namespace PanelTracker.Core.Tests;

public class ProjectScannerTests
{
    [Fact]
    public void Status_is_the_folder_where_the_file_lies()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N021.pdf");
        project.AddFile("sent", "W046.pdf");
        project.AddFile("hold", "S177.PDF");
        project.AddFile("sent", "notes.txt");

        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        Assert.Equal(3, scan.Panels.Count);
        Assert.Equal("ready_to_check", scan.FindPanel("N021")!.StatusKey);
        Assert.Equal("sent", scan.FindPanel("W046")!.StatusKey);
        Assert.Equal("hold", scan.FindPanel("S177")!.StatusKey);
        Assert.Empty(scan.MissingFolders);
    }

    [Fact]
    public void Panels_are_sorted_by_number()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N10.pdf");
        project.AddFile("ready_to_check", "N2.pdf");
        project.AddFile("ready_to_send", "N1.pdf");

        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        Assert.Equal(new[] { "N1", "N2", "N10" }, scan.Panels.Select(p => p.Id));
    }

    [Fact]
    public void Highest_revision_wins_and_is_reported()
    {
        using var project = new TempProject();
        project.AddFile("sent", "N021.pdf");
        project.AddFile("sent", "N021_R1.pdf");
        project.AddFile("ready_to_check", "N021_R2.pdf");

        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        var panel = Assert.Single(scan.Panels);
        Assert.Equal("N021", panel.Id);
        Assert.Equal(2, panel.Revision);
        Assert.Equal("ready_to_check", panel.StatusKey);
        Assert.Equal("N021_R2.pdf", panel.CurrentFile.FileName);
        Assert.Equal(3, panel.Files.Count);
    }

    [Fact]
    public void Duplicate_is_a_panel_present_in_two_status_folders()
    {
        using var project = new TempProject();
        project.AddFile("ready_to_check", "N021.pdf", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        project.AddFile("sent", "N021.pdf", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        project.AddFile("sent", "W046.pdf");

        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        var duplicate = Assert.Single(scan.Duplicates);
        Assert.Equal("N021", duplicate.Id);
        Assert.Equal(new[] { "ready_to_check", "sent" }, duplicate.DuplicateStatusKeys);
        Assert.Equal("sent", duplicate.StatusKey); // более свежий файл определяет статус
        Assert.False(scan.FindPanel("W046")!.IsDuplicate);
    }

    [Fact]
    public void Missing_status_folders_are_reported_not_fatal()
    {
        using var project = new TempProject(createStatusFolders: false);
        Directory.CreateDirectory(project.FolderOf("sent"));
        project.AddFile("sent", "N021.pdf");

        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        Assert.Single(scan.Panels);
        Assert.Equal(project.Settings.Statuses.Count - 1, scan.MissingFolders.Count);
        Assert.DoesNotContain(scan.MissingFolders, s => s.Key == "sent");
    }

    [Fact]
    public void Subfolders_are_ignored_unless_enabled()
    {
        using var project = new TempProject();
        var nested = Path.Combine(project.FolderOf("sent"), "archive");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "N099.pdf"), "x");

        Assert.Empty(ProjectScanner.Scan(project.Root, project.Settings).Panels);

        project.Settings.IncludeSubfolders = true;
        Assert.Single(ProjectScanner.Scan(project.Root, project.Settings).Panels);
    }

    [Fact]
    public void Temporary_and_hidden_files_are_skipped()
    {
        using var project = new TempProject();
        project.AddFile("sent", "~$N021.pdf");
        project.AddFile("sent", ".N022.pdf");
        project.AddFile("sent", "N023.pdf");

        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        Assert.Equal(new[] { "N023" }, scan.Panels.Select(p => p.Id));
    }

    [Fact]
    public void Statistics_count_each_status_and_sent_progress()
    {
        using var project = new TempProject();
        project.AddFile("sent", "N1.pdf");
        project.AddFile("sent", "N2.pdf");
        project.AddFile("ready_to_check", "N3.pdf");
        project.AddFile("problem", "N4.pdf");

        var scan = ProjectScanner.Scan(project.Root, project.Settings);
        var stats = BoardStatistics.Compute(scan, project.Settings);

        Assert.Equal(4, stats.Total);
        Assert.Equal(2, stats.Sent);
        Assert.Equal(50.0, stats.SentPercent);
        Assert.Equal(0, stats.Unknown);
        Assert.Equal(2, stats.ByStatus.Single(c => c.Status.Key == "sent").Count);
        Assert.Equal(25.0, stats.ByStatus.Single(c => c.Status.Key == "problem").Percent);
        Assert.Equal(0, stats.ByStatus.Single(c => c.Status.Key == "hold").Count);
    }
}
