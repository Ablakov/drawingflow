using System.Net;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.IntegrationTests.Infrastructure;

namespace DrawingFlow.IntegrationTests.Agent;

/// <summary>Серверная часть этапа 3: регистрация и отзыв агентов, границы доступа агента, папки наблюдения, очередь операций.</summary>
public sealed class AgentApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static DrawingFlowApiClient AgentClient(ApiFactory factory, string key)
    {
        var handler = new AgentTokenHandler(() => new AgentTokenRequest(key, "WS-TEST", "test"), ApiFactory.Json);
        return new DrawingFlowApiClient(factory.CreateDefaultClient(handler), new SessionManager(new InMemorySessionStore()));
    }

    [Fact]
    public async Task Agent_registration_is_admin_only_and_key_is_shown_once()
    {
        var s = await Scenario.CreateAsync(factory);

        var result = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Станция инженера"));
        Assert.True(result.AgentKey.Length >= 40);
        Assert.Equal(result.AgentKey[..8], result.Agent.CredentialPrefix);
        Assert.False(result.Agent.IsRevoked);
        Assert.False(result.Agent.IsOnline);

        var agents = await s.Owner.GetAgentsAsync(s.Organization.Id);
        Assert.Contains(agents, a => a.Id == result.Agent.Id && a.CreatedByName == s.Login("orgadmin").Replace("_", " "));

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Engineer.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Нельзя")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Manager.GetAgentsAsync(s.Organization.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetAgentsAsync(s.Organization.Id));
    }

    [Fact]
    public async Task Revoked_agent_is_rejected_immediately()
    {
        var s = await Scenario.CreateAsync(factory);
        var registration = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Отзываемый"));
        var agent = AgentClient(factory, registration.AgentKey);

        var configuration = await agent.GetAgentConfigurationAsync();
        Assert.Equal(registration.Agent.Id, configuration.AgentId);
        Assert.Empty(configuration.Folders);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.RevokeAgentAsync(registration.Agent.Id));
        var revoked = await s.OrgAdmin.RevokeAgentAsync(registration.Agent.Id);
        Assert.True(revoked.IsRevoked);

        // Действующий токен агента отклоняется сразу, а не по истечении срока.
        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => agent.GetAgentConfigurationAsync());
        Assert.Equal("agent_revoked", ex.ErrorCode);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => factory.CreateApiClient().ExchangeAgentKeyAsync(new AgentTokenRequest(registration.AgentKey)));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.OrgAdmin.RevokeAgentAsync(registration.Agent.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Unauthorized, () => factory.CreateApiClient().ExchangeAgentKeyAsync(new AgentTokenRequest("no-such-key")));
    }

    [Fact]
    public async Task Agent_acts_only_within_its_watched_folder_projects()
    {
        var s = await Scenario.CreateAsync(factory);
        var registration = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Ограниченный"));
        var agent = AgentClient(factory, registration.AgentKey);
        var otherProject = await s.OrgAdmin.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest("OTHER-" + s.Suffix, "Другой проект"));
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("АГ-1", "Документ для агента"));

        // Без папки наблюдения проект для агента не существует.
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => agent.LookupDocumentsAsync(s.Project.Id, "АГ-1", null));

        var folder = await s.Engineer.CreateWatchedFolderAsync(registration.Agent.Id, new SaveWatchedFolderRequest(s.Project.Id, @"C:\Проекты\КЖ"));
        var found = await agent.LookupDocumentsAsync(s.Project.Id, "аг-1", null);
        Assert.Single(found);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => agent.LookupDocumentsAsync(otherProject.Id, "АГ-1", null));

        var version = await agent.UploadDocumentVersionAsync(document.Id, new MemoryStream([1, 2, 3]), "АГ-1_план.dwg", null, "агент", Guid.NewGuid().ToString("N"));
        Assert.StartsWith("Агент: ", version.CreatedByName);
        Assert.Null(version.CreatedByUserId);

        // Пользовательские операции агенту недоступны.
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => agent.GetOrganizationsAsync());
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => agent.GetCurrentUserAsync());
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => agent.GetAgentsAsync(s.Organization.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => agent.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("АГ-2", "Без автозаполнения")));

        await s.Engineer.UpdateWatchedFolderAsync(folder.Id, new SaveWatchedFolderRequest(s.Project.Id, folder.Path, AutoRegisterUnknown: true));
        var registered = await agent.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("АГ-2", "Автоматически"));
        Assert.Equal(s.Login("engineer").Replace("_", " "), registered.CreatedByName);

        // Отключённая папка снимает доступ.
        await s.Engineer.UpdateWatchedFolderAsync(folder.Id, new SaveWatchedFolderRequest(s.Project.Id, folder.Path, IsEnabled: false));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => agent.LookupDocumentsAsync(s.Project.Id, "АГ-1", null));
    }

    [Fact]
    public async Task Watched_folder_rules_are_validated_and_authorized()
    {
        var s = await Scenario.CreateAsync(factory);
        var other = await Scenario.CreateAsync(factory);
        var registration = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Папки"));

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.CreateWatchedFolderAsync(registration.Agent.Id, new SaveWatchedFolderRequest(s.Project.Id, @"D:\Чертежи")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.OrgAdmin.CreateWatchedFolderAsync(registration.Agent.Id, new SaveWatchedFolderRequest(other.Project.Id, @"D:\Чужой")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => other.OrgAdmin.CreateWatchedFolderAsync(registration.Agent.Id, new SaveWatchedFolderRequest(other.Project.Id, @"D:\Чужой")));

        var invalid = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => s.Engineer.CreateWatchedFolderAsync(registration.Agent.Id,
            new SaveWatchedFolderRequest(s.Project.Id, @"D:\Чертежи", DocumentNumberPattern: "^[A-Z]+")));
        Assert.True(invalid.Errors!.ContainsKey("documentNumberPattern"));

        var folder = await s.Engineer.CreateWatchedFolderAsync(registration.Agent.Id, new SaveWatchedFolderRequest(s.Project.Id, @"D:\Чертежи\", "*.dwg", "~$*", FileMatchingRule.FileNameIsNumber));
        Assert.Equal(@"D:\Чертежи", folder.Path);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Conflict, () => s.Engineer.CreateWatchedFolderAsync(registration.Agent.Id, new SaveWatchedFolderRequest(s.Project.Id, @"d:/чертежи")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => s.Engineer.UpdateWatchedFolderAsync(folder.Id, new SaveWatchedFolderRequest(Guid.NewGuid(), folder.Path)));

        var folders = await s.Engineer.GetWatchedFoldersAsync(registration.Agent.Id);
        Assert.Single(folders);
        Assert.Equal(s.Project.Code, folders[0].ProjectCode);
        Assert.Equal(1, (await s.OrgAdmin.GetAgentsAsync(s.Organization.Id)).Single(a => a.Id == registration.Agent.Id).FolderCount);

        await s.Manager.DeleteWatchedFolderAsync(folder.Id);
        Assert.Empty(await s.Engineer.GetWatchedFoldersAsync(registration.Agent.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Manager.DeleteWatchedFolderAsync(folder.Id));
    }

    [Fact]
    public async Task Operation_reports_and_user_actions_round_trip()
    {
        var s = await Scenario.CreateAsync(factory);
        var registration = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Очередь"));
        var agent = AgentClient(factory, registration.AgentKey);
        var folder = await s.Engineer.CreateWatchedFolderAsync(registration.Agent.Id, new SaveWatchedFolderRequest(s.Project.Id, @"C:\Очередь"));
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ОЧ-1", "Целевой документ"));
        var operationId = Guid.NewGuid();

        var actions = await agent.ReportSyncOperationsAsync([new ReportSyncOperationRequest(operationId, folder.Id, s.Project.Id, @"C:\Очередь\неизвестный.dwg", "неизвестный.dwg", 123, null,
            SyncOperationStatus.NeedsResolution, 0, "Документ не найден", null, null, DateTime.UtcNow, [new SyncCandidateDto(document.Id, "ОЧ-1", "Целевой документ")])]);
        Assert.Empty(actions);

        var forAdmin = await s.OrgAdmin.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery(ActiveOnly: true));
        var operation = Assert.Single(forAdmin.Items);
        Assert.Equal(SyncOperationStatus.NeedsResolution, operation.Status);
        Assert.Equal("Очередь", operation.AgentName);
        Assert.Equal(@"C:\Очередь", operation.FolderPath);
        Assert.Single(operation.Candidates);
        Assert.Single((await s.Engineer.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery())).Items);
        Assert.Empty((await s.Viewer.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery())).Items);
        Assert.Empty((await s.Member.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery())).Items);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery()));

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.ResolveSyncOperationAsync(operationId, new ResolveSyncOperationRequest(document.Id)));
        var resolved = await s.Engineer.ResolveSyncOperationAsync(operationId, new ResolveSyncOperationRequest(document.Id));
        Assert.Equal(SyncRequestedAction.UploadToDocument, resolved.RequestedAction);
        Assert.Equal(document.Id, resolved.RequestedDocumentId);

        var pending = await agent.GetAgentActionsAsync();
        Assert.Equal(new AgentActionDto(operationId, SyncRequestedAction.UploadToDocument, document.Id), Assert.Single(pending));

        // Агент применил действие и отчитался — запрос снят; затем завершил операцию.
        await agent.ReportSyncOperationsAsync([new ReportSyncOperationRequest(operationId, folder.Id, s.Project.Id, @"C:\Очередь\неизвестный.dwg", "неизвестный.dwg", 123, null, SyncOperationStatus.Processing, 0, null, document.Id, null, DateTime.UtcNow)]);
        Assert.Empty(await agent.GetAgentActionsAsync());
        var version = await agent.UploadDocumentVersionAsync(document.Id, new MemoryStream([9, 9]), "неизвестный.dwg", null, null, operationId.ToString("N"));
        await agent.ReportSyncOperationsAsync([new ReportSyncOperationRequest(operationId, folder.Id, s.Project.Id, @"C:\Очередь\неизвестный.dwg", "неизвестный.dwg", 2, version.Sha256Hash, SyncOperationStatus.Completed, 0, null, document.Id, version.Id, DateTime.UtcNow)]);

        var completed = (await s.OrgAdmin.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery(Status: SyncOperationStatus.Completed))).Items.Single();
        Assert.Equal(1, completed.VersionNumber);
        Assert.Equal("ОЧ-1", completed.DocumentNumber);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Engineer.RetrySyncOperationAsync(operationId));

        // Ошибка → повтор; другая → отклонение с действием для агента; агент не может оживить отклонённую операцию.
        var failedId = Guid.NewGuid();
        await agent.ReportSyncOperationsAsync([new ReportSyncOperationRequest(failedId, folder.Id, s.Project.Id, @"C:\Очередь\сбой.dwg", "сбой.dwg", null, null, SyncOperationStatus.Failed, 8, "Нет связи", null, null, DateTime.UtcNow)]);
        Assert.Equal(SyncRequestedAction.Retry, (await s.Manager.RetrySyncOperationAsync(failedId)).RequestedAction);
        var dismissed = await s.Manager.DismissSyncOperationAsync(failedId);
        Assert.Equal(SyncOperationStatus.Dismissed, dismissed.Status);
        Assert.Contains(await agent.GetAgentActionsAsync(), a => a.OperationId == failedId && a.Action == SyncRequestedAction.Dismiss);
        await agent.ReportSyncOperationsAsync([new ReportSyncOperationRequest(failedId, folder.Id, s.Project.Id, @"C:\Очередь\сбой.dwg", "сбой.dwg", null, null, SyncOperationStatus.Pending, 0, null, null, null, DateTime.UtcNow)]);
        Assert.Equal(SyncOperationStatus.Dismissed, (await s.OrgAdmin.GetSyncOperationsAsync(s.Organization.Id, new SyncOperationListQuery(Status: SyncOperationStatus.Dismissed))).Items.Single().Status);

        // Отчёт по чужому проекту отклоняется.
        var other = await s.OrgAdmin.CreateProjectAsync(s.Organization.Id, new CreateProjectRequest("NOFOLDER-" + s.Suffix, "Без папки"));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => agent.ReportSyncOperationsAsync([new ReportSyncOperationRequest(Guid.NewGuid(), null, other.Id, @"C:\x", "x.dwg", null, null, SyncOperationStatus.Pending, 0, null, null, null, DateTime.UtcNow)]));
    }

    [Fact]
    public async Task Heartbeat_feeds_agent_status_and_summary()
    {
        var s = await Scenario.CreateAsync(factory);
        var registration = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Живой"));
        var agent = AgentClient(factory, registration.AgentKey);

        await agent.SendAgentHeartbeatAsync(new AgentHeartbeatRequest("WS-07", "0.3.0", 4, 1, 2));

        var dto = (await s.OrgAdmin.GetAgentsAsync(s.Organization.Id)).Single(a => a.Id == registration.Agent.Id);
        Assert.True(dto.IsOnline);
        Assert.Equal("WS-07", dto.MachineName);
        Assert.Equal((4, 1, 2), (dto.PendingOperations, dto.FailedOperations, dto.UnresolvedOperations));

        var summary = await s.Viewer.GetSyncSummaryAsync(s.Organization.Id);
        Assert.Equal(1, summary.AgentsTotal);
        Assert.Equal(1, summary.AgentsOnline);
        Assert.NotNull(summary.LastHeartbeatAtUtc);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetSyncSummaryAsync(s.Organization.Id));
    }
}
