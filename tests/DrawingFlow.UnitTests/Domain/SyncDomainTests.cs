using DrawingFlow.Domain.Entities;
using DrawingFlow.Domain.Enums;
using DrawingFlow.Domain.Exceptions;

namespace DrawingFlow.UnitTests.Domain;

public sealed class SyncDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static AgentInstallation NewAgent() => AgentInstallation.Create(Guid.NewGuid(), "Станция КЖ-1", Hash, "abcdefgh-ignored", Guid.NewGuid(), Now);

    private static WatchedFolder NewFolder(string path = @"C:\Проекты\КЖ\", FileMatchingRule rule = FileMatchingRule.NumberFromFileName, string? pattern = null, bool auto = false) =>
        WatchedFolder.Create(Guid.NewGuid(), Guid.NewGuid(), path, "*.dwg;*.pdf", "~$*;*.bak", rule, pattern, auto, true, Guid.NewGuid(), Now);

    private static SyncOperation NewOperation() =>
        SyncOperation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), @"C:\Проекты\КЖ\АР-01_план.dwg", "АР-01_план.dwg", Now, Now);

    [Fact]
    public void Agent_credential_prefix_is_truncated_and_revocation_is_final()
    {
        var agent = NewAgent();
        Assert.Equal("abcdefgh", agent.CredentialPrefix);
        Assert.False(agent.IsRevoked);
        Assert.False(agent.IsOnline(Now));

        agent.RecordHeartbeat(Now, "WS-01", "0.1.0", 3, 1, 2);
        Assert.True(agent.IsOnline(Now.AddMinutes(2)));
        Assert.False(agent.IsOnline(Now.AddMinutes(4)));
        Assert.Equal((3, 1, 2), (agent.PendingOperations, agent.FailedOperations, agent.UnresolvedOperations));

        agent.Revoke(Guid.NewGuid(), Now.AddMinutes(1));
        Assert.True(agent.IsRevoked);
        Assert.False(agent.IsOnline(Now.AddMinutes(1)));
        Assert.Throws<DomainRuleException>(() => agent.Revoke(Guid.NewGuid(), Now.AddMinutes(2)));
    }

    [Fact]
    public void Folder_path_is_normalized_for_case_insensitive_uniqueness()
    {
        var folder = NewFolder(@"c:\Проекты\кж\");
        Assert.Equal(@"c:\Проекты\кж", folder.Path);
        Assert.Equal(WatchedFolder.NormalizePath(@"C:/ПРОЕКТЫ/КЖ"), folder.NormalizedPath);
        Assert.Equal("*.dwg;*.pdf", folder.IncludePatterns);
        Assert.Null(folder.DocumentNumberPattern);
    }

    [Fact]
    public void Folder_number_pattern_must_be_valid_regex_with_number_group()
    {
        NewFolder(pattern: @"^(?<number>[А-Я]+-\d+)");
        var noGroup = Assert.Throws<ValidationException>(() => NewFolder(pattern: @"^[А-Я]+-\d+"));
        Assert.True(noGroup.Errors.ContainsKey("documentNumberPattern"));
        Assert.Throws<ValidationException>(() => NewFolder(pattern: "(("));
        // Для других правил шаблон не проверяется как регулярное выражение.
        NewFolder(rule: FileMatchingRule.FileNameIsNumber, pattern: "((");
    }

    [Fact]
    public void Folder_rejects_empty_or_control_paths()
    {
        Assert.Throws<ValidationException>(() => NewFolder(" "));
        Assert.Throws<ValidationException>(() => NewFolder("C:\\bad\tpath"));
        Assert.Throws<ValidationException>(() => NewFolder(new string('x', WatchedFolder.PathMaxLength + 1)));
    }

    [Fact]
    public void Operation_report_updates_state_and_clears_request_only_on_progress()
    {
        var operation = NewOperation();
        var user = Guid.NewGuid();
        Assert.Equal(SyncOperationStatus.Pending, operation.Status);

        operation.ApplyReport(SyncOperationStatus.NeedsResolution, 10, Hash.ToUpperInvariant(), 0, "Найдено 2 документа", null, null, "[]", Now.AddMinutes(1));
        Assert.Equal(Hash, operation.Sha256Hash);
        Assert.False(operation.IsTerminal);

        var documentId = Guid.NewGuid();
        operation.RequestUpload(documentId, user, Now.AddMinutes(2));
        Assert.Equal(SyncRequestedAction.UploadToDocument, operation.RequestedAction);
        Assert.Equal(documentId, operation.RequestedDocumentId);

        // Агент ещё не увидел запрос и повторил прежнее состояние — запрос сохраняется.
        operation.ApplyReport(SyncOperationStatus.NeedsResolution, 10, Hash, 0, "Найдено 2 документа", null, null, "[]", Now.AddMinutes(3));
        Assert.Equal(SyncRequestedAction.UploadToDocument, operation.RequestedAction);

        // Агент применил запрос и двинулся дальше — запрос снимается.
        operation.ApplyReport(SyncOperationStatus.Processing, 10, Hash, 0, null, documentId, null, null, Now.AddMinutes(4));
        Assert.Equal(SyncRequestedAction.None, operation.RequestedAction);
        Assert.Null(operation.RequestedDocumentId);

        var versionId = Guid.NewGuid();
        operation.ApplyReport(SyncOperationStatus.Completed, 10, Hash, 0, null, documentId, versionId, null, Now.AddMinutes(5));
        Assert.True(operation.IsTerminal);
        Assert.Equal(Now.AddMinutes(5), operation.CompletedAtUtc);
        Assert.Equal(versionId, operation.DocumentVersionId);
    }

    [Fact]
    public void Operation_actions_follow_rules()
    {
        var operation = NewOperation();
        var user = Guid.NewGuid();
        Assert.Throws<DomainRuleException>(() => operation.RequestRetry(user, Now));

        operation.ApplyReport(SyncOperationStatus.Failed, null, null, 8, "Нет соединения", null, null, null, Now);
        operation.RequestRetry(user, Now.AddMinutes(1));
        Assert.Equal(SyncRequestedAction.Retry, operation.RequestedAction);

        operation.Dismiss(user, Now.AddMinutes(2));
        Assert.Equal(SyncOperationStatus.Dismissed, operation.Status);
        Assert.Equal(SyncRequestedAction.Dismiss, operation.RequestedAction);
        Assert.Throws<DomainRuleException>(() => operation.Dismiss(user, Now.AddMinutes(3)));

        // Отчёт агента не оживляет отклонённую операцию.
        operation.ApplyReport(SyncOperationStatus.Pending, null, null, 0, null, null, null, null, Now.AddMinutes(4));
        Assert.Equal(SyncOperationStatus.Dismissed, operation.Status);

        Assert.Throws<ValidationException>(() => NewOperation().ApplyReport(SyncOperationStatus.Dismissed, null, null, 0, null, null, null, null, Now));
        Assert.Throws<ValidationException>(() => SyncOperation.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "p", "f", Now, Now));
    }
}
