using DrawingFlow.Agent;
using DrawingFlow.Agent.Queue;
using DrawingFlow.Agent.Sync;
using DrawingFlow.Contracts.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DrawingFlow.UnitTests.Agent;

public sealed class FileMatcherTests
{
    private static WatchedFolderDto Folder(FileMatchingRule rule = FileMatchingRule.NumberFromFileName, string? pattern = null, string include = "*.dwg;*.pdf", string? exclude = "~$*;*.bak") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "P", "Проект", @"C:\Чертежи", include, exclude, rule, pattern, false, true, DateTime.UtcNow, DateTime.UtcNow);

    [Theory]
    [InlineData("АР-01_план.dwg", true)]
    [InlineData("АР-01_план.PDF", true)]
    [InlineData("АР-01_план.docx", false)]
    [InlineData("~$АР-01_план.dwg", false)]
    [InlineData("АР-01_план.bak", false)]
    [InlineData("АР-01_план.tmp", false)]
    [InlineData(".hidden.dwg", false)]
    public void Eligibility_respects_include_exclude_and_global_patterns(string fileName, bool expected)
    {
        var always = new AgentOptions().AlwaysExcludePatterns;
        Assert.Equal(expected, FileMatcher.IsEligible(fileName, Folder(), always));
    }

    [Fact]
    public void Wildcard_include_accepts_everything_except_exclusions()
    {
        Assert.True(FileMatcher.IsEligible("любой файл.xyz", Folder(include: "*"), []));
        Assert.False(FileMatcher.IsEligible("~$x.docx", Folder(include: "*"), ["~$*"]));
    }

    [Theory]
    [InlineData("АР-01_план фундаментов.dwg", "АР-01")]
    [InlineData("КЖ-12.3 схема.pdf", "КЖ-12.3")]
    [InlineData("план.dwg", "план")]
    [InlineData("_без номера.dwg", null)]
    public void Default_pattern_takes_token_before_space_or_underscore(string fileName, string? expected)
    {
        Assert.Equal(expected, FileMatcher.ExtractDocumentNumber(Folder(), fileName));
    }

    [Fact]
    public void Custom_pattern_and_other_rules()
    {
        var custom = Folder(pattern: @"^\d+-(?<number>[А-Я]+-\d+)");
        Assert.Equal("АР-7", FileMatcher.ExtractDocumentNumber(custom, "2026-АР-7_разрез.dwg"));
        Assert.Null(FileMatcher.ExtractDocumentNumber(custom, "АР-7_разрез.dwg"));
        Assert.Equal("АР-01_план", FileMatcher.ExtractDocumentNumber(Folder(FileMatchingRule.FileNameIsNumber), "АР-01_план.dwg"));
        Assert.Null(FileMatcher.ExtractDocumentNumber(Folder(FileMatchingRule.VersionFileName), "АР-01_план.dwg"));
        Assert.Null(FileMatcher.ExtractDocumentNumber(Folder(pattern: "(("), "АР-01.dwg"));
    }
}

public sealed class RetryPolicyTests
{
    [Fact]
    public void Delays_double_and_are_capped()
    {
        var options = new AgentOptions { InitialRetryDelay = TimeSpan.FromSeconds(10), MaxRetryDelay = TimeSpan.FromMinutes(15), MaxRetries = 8 };
        Assert.Equal(TimeSpan.FromSeconds(10), RetryPolicy.Delay(1, options));
        Assert.Equal(TimeSpan.FromSeconds(20), RetryPolicy.Delay(2, options));
        Assert.Equal(TimeSpan.FromSeconds(80), RetryPolicy.Delay(4, options));
        Assert.Equal(TimeSpan.FromMinutes(15), RetryPolicy.Delay(12, options));
        Assert.Equal(TimeSpan.FromMinutes(15), RetryPolicy.Delay(40, options));
        Assert.False(RetryPolicy.IsExhausted(8, options));
        Assert.True(RetryPolicy.IsExhausted(9, options));
    }

    [Fact]
    public void Options_validation_rejects_nonsense()
    {
        Assert.Throws<InvalidOperationException>(() => new AgentOptions { StabilityWindow = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AgentOptions { MaxRetryDelay = TimeSpan.FromSeconds(1), InitialRetryDelay = TimeSpan.FromSeconds(5) }.Validate());
        new AgentOptions().Validate();
    }
}

public sealed class SyncQueueTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "drawingflow-queue-tests", Guid.NewGuid().ToString("N"));
    private SyncQueue _queue = null!;
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Factory(string path) : IDbContextFactory<AgentQueueDbContext>
    {
        public AgentQueueDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AgentQueueDbContext>().UseSqlite($"Data Source={path}").Options);
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _queue = new SyncQueue(new Factory(Path.Combine(_root, "queue.db")), NullLogger<SyncQueue>.Instance);
        await _queue.InitializeAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Duplicate_candidates_share_one_active_operation()
    {
        var folder = Guid.NewGuid();
        var first = await _queue.EnqueueCandidateAsync(folder, Guid.NewGuid(), @"C:\a\АР-1.dwg", "АР-1.dwg", 10, Now, Now, CancellationToken.None);
        var second = await _queue.EnqueueCandidateAsync(folder, Guid.NewGuid(), @"C:\a\АР-1.dwg", "АР-1.dwg", 12, Now.AddSeconds(1), Now.AddSeconds(1), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first.Id, second!.Id);
        Assert.Equal(12, second.LastObservedSize);
        Assert.Single(await _queue.ListAsync(CancellationToken.None));
        Assert.Equal((1, 0, 0), await _queue.CountsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Failed_operation_is_requeued_by_new_change_and_known_file_state_is_skipped()
    {
        var operation = (await _queue.EnqueueCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), @"C:\a\КЖ-1.dwg", "КЖ-1.dwg", 10, Now, Now, CancellationToken.None))!;
        operation.Status = SyncOperationStatus.Failed;
        operation.RetryCount = 9;
        await _queue.SaveAsync(operation, CancellationToken.None);

        var requeued = await _queue.EnqueueCandidateAsync(operation.WatchedFolderId, operation.ProjectId, operation.FilePath, operation.FileName, 11, Now.AddMinutes(1), Now.AddMinutes(1), CancellationToken.None);
        Assert.Equal(SyncOperationStatus.Pending, requeued!.Status);
        Assert.Equal(0, requeued.RetryCount);

        requeued.Status = SyncOperationStatus.Completed;
        requeued.CompletedAtUtc = Now.AddMinutes(2);
        await _queue.SaveAsync(requeued, CancellationToken.None);
        await _queue.UpsertFileStateAsync(new FileState { FilePath = operation.FilePath, WatchedFolderId = operation.WatchedFolderId, Sha256 = "abc", Size = 11, ModifiedAtUtc = Now.AddMinutes(1), DocumentId = Guid.NewGuid(), DocumentVersionId = Guid.NewGuid(), UpdatedAtUtc = Now }, CancellationToken.None);

        Assert.Null(await _queue.EnqueueCandidateAsync(operation.WatchedFolderId, operation.ProjectId, operation.FilePath, operation.FileName, 11, Now.AddMinutes(1), Now.AddMinutes(3), CancellationToken.None));
        Assert.NotNull(await _queue.EnqueueCandidateAsync(operation.WatchedFolderId, operation.ProjectId, operation.FilePath, operation.FileName, 12, Now.AddMinutes(4), Now.AddMinutes(4), CancellationToken.None));
    }

    [Fact]
    public async Task Due_recovery_and_reporting_flags()
    {
        var a = (await _queue.EnqueueCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), @"C:\a\1.dwg", "1.dwg", 1, Now, Now, CancellationToken.None))!;
        var b = (await _queue.EnqueueCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), @"C:\a\2.dwg", "2.dwg", 1, Now, Now, CancellationToken.None))!;
        a.NextAttemptAtUtc = Now.AddMinutes(5);
        await _queue.SaveAsync(a, CancellationToken.None);
        b.Status = SyncOperationStatus.Processing;
        await _queue.SaveAsync(b, CancellationToken.None);

        Assert.Empty(await _queue.GetDueAsync(Now, 10, CancellationToken.None));
        Assert.Equal(1, await _queue.RecoverInFlightAsync(Now, CancellationToken.None));
        Assert.Single(await _queue.GetDueAsync(Now, 10, CancellationToken.None));
        Assert.Equal(2, (await _queue.GetDueAsync(Now.AddMinutes(6), 10, CancellationToken.None)).Count);

        Assert.Equal(2, (await _queue.GetUnreportedAsync(10, CancellationToken.None)).Count);
        await _queue.MarkReportedAsync([a.Id], CancellationToken.None);
        Assert.Single(await _queue.GetUnreportedAsync(10, CancellationToken.None));

        await _queue.SetSettingAsync("k", "v", CancellationToken.None);
        Assert.Equal("v", await _queue.GetSettingAsync("k", CancellationToken.None));
        Assert.Null(await _queue.GetSettingAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task Queue_persists_across_instances()
    {
        var path = Path.Combine(_root, "queue.db");
        await _queue.EnqueueCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), @"C:\a\x.dwg", "x.dwg", 1, Now, Now, CancellationToken.None);

        var reopened = new SyncQueue(new Factory(path), NullLogger<SyncQueue>.Instance);
        await reopened.InitializeAsync(CancellationToken.None);
        Assert.Single(await reopened.ListAsync(CancellationToken.None));
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }
}
