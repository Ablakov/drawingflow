using PanelTracker.Core.Groups;

namespace PanelTracker.Core.Tests;

public class GroupStoreTests
{
    [Fact]
    public void Uses_default_groups_when_file_is_missing()
    {
        using var project = new TempProject();
        var store = new GroupStore(Path.Combine(project.Root, GroupStore.FileName));

        store.Load();

        Assert.Equal(new[] { "A", "B", "C", "D" }, store.Groups.Select(g => g.Key));
        Assert.Equal("A: Solid panel", store.Groups[0].DisplayName);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void Adds_removes_and_persists_panels()
    {
        using var project = new TempProject();
        var path = Path.Combine(project.Root, GroupStore.FileName);
        var store = new GroupStore(path);
        store.Load();

        Assert.Equal(2, store.AddPanels("A", ["N10", "N2"]));
        Assert.Equal(0, store.AddPanels("A", ["n2"]));
        store.AddGroup("E", "Parapet");
        store.AddPanels("E", ["N2"]);
        store.RenameGroup("B", "Spandrel panel");
        Assert.True(store.RemoveGroup("D"));
        store.Save();

        var reloaded = new GroupStore(path);
        reloaded.Load();

        Assert.Equal(new[] { "N2", "N10" }, reloaded.Find("A")!.Panels);
        Assert.Equal("Spandrel panel", reloaded.Find("B")!.Name);
        Assert.Null(reloaded.Find("D"));
        Assert.Equal(new[] { "A", "E" }, reloaded.GroupsOf("N2").Select(g => g.Key));
        Assert.Equal(1, reloaded.RemovePanels("A", ["N2"]));
        Assert.Equal(new[] { "N10" }, reloaded.Find("A")!.Panels);
    }

    [Fact]
    public void Rejects_duplicate_group_key()
    {
        using var project = new TempProject();
        var store = new GroupStore(Path.Combine(project.Root, GroupStore.FileName));
        store.Load();

        Assert.Throws<InvalidOperationException>(() => store.AddGroup("a", "Другая"));
    }
}
