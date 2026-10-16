using DrawingFlow.Client;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.IntegrationTests.Infrastructure;

namespace DrawingFlow.IntegrationTests.Agent;

/// <summary>
/// Критерий приёмки этапа 3: стабильное изменение файла в настроенной папке обнаруживается и регистрируется на сервере ровно один раз.
/// Ядро агента работает против настоящего сервера и настоящей файловой системы; таймеры заменены явными вызовами.
/// </summary>
public sealed class AgentPipelineTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Stable_file_is_registered_exactly_once_and_unchanged_files_are_skipped()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("АР-01", "План"));
        h.WriteFile("АР-01_план.txt", "содержимое чертежа v1");

        Assert.Equal(1, await h.Engine.ReconcileAsync(CancellationToken.None));
        // Первый проход лишь наблюдает стабильность.
        await h.Engine.ProcessDueAsync(CancellationToken.None);
        Assert.Equal(0, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);

        var counts = await h.RunUntilSettledAsync();
        Assert.Equal((0, 0, 0), counts);
        var operation = await h.SingleOperationAsync();
        Assert.NotNull(operation);
        Assert.Equal(SyncOperationStatus.Completed, operation.Status);

        var reloaded = await s.Viewer.GetDocumentAsync(document.Id);
        Assert.Equal(1, reloaded.VersionCount);
        Assert.Equal(operation.DocumentVersionId, reloaded.CurrentVersion!.Id);
        Assert.Equal(operation.Sha256, reloaded.CurrentVersion.Sha256Hash);
        Assert.StartsWith("Агент: ", reloaded.CurrentVersion.CreatedByName);

        // Повторная сверка без изменений не порождает ни операций, ни версий; события-дубликаты тоже.
        Assert.Equal(0, await h.Engine.ReconcileAsync(CancellationToken.None));
        for (var i = 0; i < 5; i++)
        {
            await h.Engine.NotifyChangeAsync(h.Folder.Id, Path.Combine(h.WatchedPath, "АР-01_план.txt"), CancellationToken.None);
        }

        await h.RunUntilSettledAsync();
        Assert.Single(await h.Queue.ListAsync(CancellationToken.None));
        Assert.Equal(1, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);

        // Отчёт серверу: операция видна в очереди организации как завершённая.
        Assert.True(await h.Engine.ReportAsync(CancellationToken.None) > 0);
        var serverOperation = (await s.OrgAdmin.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery(AgentId: h.Registration.Agent.Id))).Items.Single();
        Assert.Equal(SyncOperationStatus.Completed, serverOperation.Status);
        Assert.Equal(1, serverOperation.VersionNumber);
        Assert.Equal("АР-01", serverOperation.DocumentNumber);
    }

    [Fact]
    public async Task Modified_content_creates_new_version_but_touch_without_change_does_not()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("КЖ-7", "Схема"));
        var path = h.WriteFile("КЖ-7_схема.txt", "версия 1");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.RunUntilSettledAsync();
        Assert.Equal(1, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);

        File.WriteAllText(path, "версия 2 — изменённое содержимое");
        Assert.Equal(1, await h.Engine.ReconcileAsync(CancellationToken.None));
        await h.RunUntilSettledAsync();
        var afterChange = await s.Viewer.GetDocumentAsync(document.Id);
        Assert.Equal(2, afterChange.VersionCount);
        Assert.Equal(2, afterChange.CurrentVersion!.VersionNumber);

        // Только время изменения: содержимое то же — новая версия не создаётся.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.RunUntilSettledAsync();
        Assert.Equal(2, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.All(await h.Queue.ListAsync(CancellationToken.None), o => Assert.Equal(SyncOperationStatus.Completed, o.Status));
    }

    [Fact]
    public async Task Unknown_file_waits_for_resolution_and_user_decision_is_applied()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var target = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ЦЕЛЬ-1", "Документ-цель"));
        h.WriteFile("XX-99_неизвестный.txt", "неизвестный файл");

        await h.Engine.ReconcileAsync(CancellationToken.None);
        var counts = await h.RunUntilSettledAsync();
        Assert.Equal(1, counts.NeedsResolution);
        var operation = await h.SingleOperationAsync();
        Assert.Contains("не найден", operation!.LastError);
        Assert.Equal(0, (await s.Viewer.GetDocumentAsync(target.Id)).VersionCount);

        await h.Engine.ReportAsync(CancellationToken.None);
        var serverOperation = (await s.Engineer.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery(Status: SyncOperationStatus.NeedsResolution))).Items.Single(o => o.Id == operation.Id);
        await s.Engineer.ResolveSyncOperationAsync(serverOperation.Id, new ResolveSyncOperationRequest(target.Id));

        // Сердцебиение приносит действие пользователя; операция возвращается в очередь и загружается в выбранный документ.
        Assert.True(await h.Engine.HeartbeatAsync(CancellationToken.None));
        Assert.Equal(SyncOperationStatus.Pending, (await h.Queue.GetAsync(operation.Id, CancellationToken.None))!.Status);
        await h.RunUntilSettledAsync();
        var done = await h.Queue.GetAsync(operation.Id, CancellationToken.None);
        Assert.Equal(SyncOperationStatus.Completed, done!.Status);
        Assert.Equal(1, (await s.Viewer.GetDocumentAsync(target.Id)).VersionCount);
    }

    [Fact]
    public async Task Auto_registration_creates_document_for_unknown_number()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s, autoRegister: true);
        h.WriteFile("НОВ-15_узел.txt", "новый документ");

        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.RunUntilSettledAsync();

        var documents = await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery(Search: "НОВ-15"));
        var created = Assert.Single(documents.Items);
        Assert.Equal("НОВ-15", created.DocumentNumber);
        Assert.Equal("НОВ-15_узел", created.Title);
        Assert.Equal(1, created.VersionCount);
        Assert.Equal(DocumentStatus.InProgress, created.Status);
    }

    [Fact]
    public async Task Locked_file_is_retried_until_released()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("БЛК-1", "Заблокированный"));
        var path = h.WriteFile("БЛК-1_файл.txt", "пишется другим приложением");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.Engine.ProcessDueAsync(CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromSeconds(2));

        LocalOperationSnapshot afterLock;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await h.Engine.ProcessDueAsync(CancellationToken.None);
            var locked = (await h.SingleOperationAsync())!;
            afterLock = new LocalOperationSnapshot(locked.Status, locked.RetryCount, locked.LastError);
        }

        Assert.Equal(SyncOperationStatus.Pending, afterLock.Status);
        Assert.Equal(1, afterLock.RetryCount);
        Assert.Contains("занят", afterLock.LastError);
        Assert.Equal(0, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);

        h.Clock.Advance(TimeSpan.FromSeconds(10));
        await h.RunUntilSettledAsync();
        Assert.Equal(1, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);
    }

    private sealed record LocalOperationSnapshot(SyncOperationStatus Status, int RetryCount, string? LastError);

    [Fact]
    public async Task Queue_survives_restart_and_in_flight_operations_are_recovered()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ПЕР-1", "Перезапуск"));
        h.WriteFile("ПЕР-1_файл.txt", "до перезапуска");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.Engine.ProcessDueAsync(CancellationToken.None);

        // Имитируем остановку процесса посреди обработки.
        var operation = (await h.SingleOperationAsync())!;
        operation.Status = SyncOperationStatus.Processing;
        await h.Queue.SaveAsync(operation, CancellationToken.None);

        await h.RestartAsync();
        var recovered = (await h.SingleOperationAsync())!;
        Assert.Equal(SyncOperationStatus.Pending, recovered.Status);

        h.Clock.Advance(TimeSpan.FromSeconds(2));
        await h.RunUntilSettledAsync();
        Assert.Equal(1, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.Equal(SyncOperationStatus.Completed, (await h.SingleOperationAsync())!.Status);
    }

    [Fact]
    public async Task Network_loss_backs_off_and_recovers_without_duplicates()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("СЕТЬ-1", "Сеть"));
        h.WriteFile("СЕТЬ-1_файл.txt", "передача при обрыве");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.Engine.ProcessDueAsync(CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromSeconds(2));

        h.Network.Offline = true;
        await h.Engine.ProcessDueAsync(CancellationToken.None);
        var failing = (await h.SingleOperationAsync())!;
        Assert.Equal(SyncOperationStatus.Pending, failing.Status);
        Assert.Equal(1, failing.RetryCount);
        Assert.True(failing.NextAttemptAtUtc > h.Clock.UtcNow);
        Assert.Contains("Нет связи", failing.LastError);
        Assert.False(await h.Engine.HeartbeatAsync(CancellationToken.None));

        // До срока повтора операция не трогается.
        await h.Engine.ProcessDueAsync(CancellationToken.None);
        Assert.Equal(1, (await h.SingleOperationAsync())!.RetryCount);

        h.Network.Offline = false;
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        await h.RunUntilSettledAsync();
        var reloaded = await s.Viewer.GetDocumentAsync(document.Id);
        Assert.Equal(1, reloaded.VersionCount);
        Assert.True(await h.Engine.HeartbeatAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Retries_are_bounded_and_end_in_failed_state()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ОТК-1", "Отказ"));
        h.WriteFile("ОТК-1_файл.txt", "сервер недоступен долго");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.Engine.ProcessDueAsync(CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromSeconds(2));

        h.Network.Offline = true;
        var counts = await h.RunUntilSettledAsync(maxCycles: 20);
        Assert.Equal(1, counts.Failed);
        var failed = (await h.SingleOperationAsync())!;
        Assert.Equal(h.Options.MaxRetries + 1, failed.RetryCount);
        Assert.Null(failed.NextAttemptAtUtc);

        // Повтор по запросу пользователя после восстановления связи.
        h.Network.Offline = false;
        await h.Engine.ReportAsync(CancellationToken.None);
        await s.Engineer.RetrySyncOperationAsync(failed.Id);
        await h.Engine.HeartbeatAsync(CancellationToken.None);
        await h.RunUntilSettledAsync();
        Assert.Equal(SyncOperationStatus.Completed, (await h.SingleOperationAsync())!.Status);
    }

    [Fact]
    public async Task Revoked_agent_pauses_without_losing_queue()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ОТЗ-1", "Отзыв"));
        h.WriteFile("ОТЗ-1_файл.txt", "после отзыва");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.Engine.ProcessDueAsync(CancellationToken.None);
        h.Clock.Advance(TimeSpan.FromSeconds(2));

        await s.OrgAdmin.RevokeAgentAsync(h.Registration.Agent.Id);
        await h.Engine.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(AgentAuthState.Revoked, h.Auth.State);
        Assert.True(h.Engine.IsRevoked);
        var operation = (await h.SingleOperationAsync())!;
        Assert.Equal(SyncOperationStatus.Pending, operation.Status);
        Assert.Contains("отозван", operation.LastError);
        Assert.Equal(0, await h.Engine.ProcessDueAsync(CancellationToken.None));
        Assert.False(await h.Engine.HeartbeatAsync(CancellationToken.None));
        Assert.Equal(0, (await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery(Search: "ОТЗ-1"))).Items.Single().VersionCount);
    }

    [Fact]
    public async Task Unavailable_folder_and_deleted_file_are_handled_safely()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var missing = await s.Engineer.CreateWatchedFolderAsync(h.Registration.Agent.Id,
            new SaveWatchedFolderRequest(s.Project.Id, Path.Combine(h.Root, "нет-такой-папки")));
        await h.Engine.RefreshConfigurationAsync(CancellationToken.None);

        await h.Engine.ReconcileAsync(CancellationToken.None);
        var status = h.Engine.FolderStatuses.Single(f => f.FolderId == missing.Id);
        Assert.False(status.IsAvailable);
        Assert.True(h.Engine.FolderStatuses.Single(f => f.FolderId == h.Folder.Id).IsAvailable);

        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("УД-1", "Удаляемый"));
        var path = h.WriteFile("УД-1_файл.txt", "будет удалён");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        File.Delete(path);
        await h.RunUntilSettledAsync();

        var operation = (await h.Queue.ListAsync(CancellationToken.None)).Single();
        Assert.Equal(SyncOperationStatus.Cancelled, operation.Status);
        Assert.Equal(0, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.Equal(1, (await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery(Search: "УД-1"))).TotalCount);
    }

    [Fact]
    public async Task Temporary_and_excluded_files_are_ignored()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        h.WriteFile("~$АР-01_план.txt", "lock-файл Office");
        h.WriteFile("АР-01_план.tmp", "временный");
        h.WriteFile("АР-01_план.bak", "резервная копия");
        h.WriteFile("АР-01_план.xyz", "не по маске");

        Assert.Equal(0, await h.Engine.ReconcileAsync(CancellationToken.None));
        Assert.Empty(await h.Queue.ListAsync(CancellationToken.None));
    }
}
