using PanelTracker.Core.Watching;

namespace PanelTracker.Core.Tests;

public class ProjectWatcherTests
{
    [Fact]
    public async Task Raises_change_after_file_is_moved()
    {
        using var project = new TempProject();
        var path = project.AddFile("ready_to_check", "N021.pdf");
        using var watcher = new ProjectWatcher(project.Root, TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(5));
        var signal = new TaskCompletionSource<WatchTrigger>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.ChangesDetected += t => signal.TrySetResult(t);
        watcher.Start();
        await Task.Delay(300); // наблюдателю нужно время на запуск

        File.Move(path, Path.Combine(project.FolderOf("sent"), "N021.pdf"));

        var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(signal.Task, completed);
        Assert.Equal(WatchTrigger.FileSystemEvent, await signal.Task);
    }

    [Fact]
    public async Task Periodic_timer_fires_without_file_events()
    {
        using var project = new TempProject();
        using var watcher = new ProjectWatcher(project.Root, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(150));
        var signal = new TaskCompletionSource<WatchTrigger>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.ChangesDetected += t =>
        {
            if (t == WatchTrigger.Periodic)
            {
                signal.TrySetResult(t);
            }
        };
        watcher.Start();

        var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.Same(signal.Task, completed);
    }

    [Fact]
    public void Stop_and_dispose_are_safe_to_call_twice()
    {
        using var project = new TempProject();
        var watcher = new ProjectWatcher(project.Root, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        watcher.Start();
        Assert.True(watcher.IsWatching);
        watcher.Stop();
        watcher.Stop();
        Assert.False(watcher.IsWatching);
        watcher.Dispose();
        watcher.Dispose();
    }
}
