using PanelTracker.Core.History;

namespace PanelTracker.Core.Tests;

public class HistoryLogTests
{
    private static HistoryEntry Entry(string panel, string? from, string to, HistoryAction action = HistoryAction.Moved, string? comment = null) => new()
    {
        Timestamp = DateTimeOffset.Now,
        User = "tester",
        PanelId = panel,
        FileName = panel + ".pdf",
        FromStatus = from,
        ToStatus = to,
        Action = action,
        Comment = comment,
    };

    [Fact]
    public void Appends_one_json_line_per_entry_and_reads_back()
    {
        using var project = new TempProject();
        var log = new HistoryLog(Path.Combine(project.Root, HistoryLog.FileName));

        log.Append(Entry("N021", "ready_to_check", "sent"));
        log.Append(Entry("W046", "sent", "problem", HistoryAction.ProblemMarked, "нет размеров"));

        var lines = File.ReadAllLines(log.FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"action\":\"problemMarked\"", lines[1]);

        var all = log.ReadAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("tester", all[0].User);
        Assert.Equal("нет размеров", all[1].Comment);
        Assert.Single(log.ReadForPanel("n021"));
        Assert.Equal("нет размеров", log.LatestComment("W046", HistoryAction.ProblemMarked));
    }

    [Fact]
    public void Skips_corrupted_lines()
    {
        using var project = new TempProject();
        var log = new HistoryLog(Path.Combine(project.Root, HistoryLog.FileName));
        log.Append(Entry("N1", null, "sent"));
        File.AppendAllText(log.FilePath, "{ this is not json\n\n");
        log.Append(Entry("N2", null, "sent"));

        Assert.Equal(new[] { "N1", "N2" }, log.ReadAll().Select(e => e.PanelId));
    }

    [Fact]
    public void Finds_status_before_hold()
    {
        using var project = new TempProject();
        var log = new HistoryLog(Path.Combine(project.Root, HistoryLog.FileName));
        log.Append(Entry("N1", "ready_to_check", "ready_to_send"));
        log.Append(Entry("N1", "ready_to_send", "hold", HistoryAction.Held));
        log.Append(Entry("N1", "hold", "hold", HistoryAction.ProblemMarked));

        Assert.Equal("ready_to_send", log.StatusBeforeLastTransitionTo("N1", "hold"));
        Assert.Null(log.StatusBeforeLastTransitionTo("N2", "hold"));
    }
}
