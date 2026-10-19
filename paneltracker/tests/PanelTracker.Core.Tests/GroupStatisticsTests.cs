using PanelTracker.Core.Groups;
using PanelTracker.Core.Models;
using PanelTracker.Core.Scanning;

namespace PanelTracker.Core.Tests;

public class GroupStatisticsTests
{
    [Fact]
    public void Counts_panels_per_group_and_status()
    {
        using var project = new TempProject();
        project.AddFile("sent", "N1.pdf");
        project.AddFile("sent", "N2.pdf");
        project.AddFile("ready_to_check", "N3.pdf");
        project.AddFile("problem", "N4.pdf");
        var groups = new List<PanelGroup>
        {
            new() { Key = "A", Name = "Solid panel", Panels = ["N1", "N3"] },
            new() { Key = "B", Name = "Spandrel", Panels = ["N1"] },
        };
        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        var rows = GroupStatistics.Compute(scan, groups, project.Settings);

        Assert.Equal(new[] { "A: Solid panel", "B: Spandrel", "Без группы", "Всего" }, rows.Select(r => r.Title));
        var a = rows[0];
        Assert.Equal(2, a.Total);
        Assert.Equal(1, a.Sent);
        Assert.Equal(50.0, a.SentPercent);
        Assert.Equal(1, a.CountsByStatus[project.Settings.OrderOf("sent")]);
        Assert.Equal(1, a.CountsByStatus[project.Settings.OrderOf("ready_to_check")]);
        Assert.Equal(1, rows[1].Total); // панель в двух группах считается в каждой
        var without = rows[2];
        Assert.Equal(2, without.Total);
        Assert.Equal(1, without.CountsByStatus[project.Settings.OrderOf("problem")]);
        var total = rows[3];
        Assert.Equal(4, total.Total);
        Assert.Equal(2, total.Sent);
        Assert.Equal(project.Settings.Statuses.Count, total.CountsByStatus.Count);
    }

    [Fact]
    public void Empty_project_gives_zero_rows_without_division_errors()
    {
        using var project = new TempProject();
        var scan = ProjectScanner.Scan(project.Root, project.Settings);

        var rows = GroupStatistics.Compute(scan, GroupStore.CreateDefaultGroups(), project.Settings);

        Assert.Equal(6, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, r.SentPercent));
    }
}
