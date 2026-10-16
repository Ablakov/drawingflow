using System.Net;
using System.Security.Cryptography;
using System.Text;
using DrawingFlow.Client;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.IntegrationTests.Infrastructure;

namespace DrawingFlow.IntegrationTests.Api;

/// <summary>Документы и неизменяемые версии: регистрация, загрузка, идемпотентность, конкурентность, доступ к скачиванию, хранилище.</summary>
public sealed class DocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static Task<DocumentVersionDto> UploadAsync(DrawingFlowApiClient client, Guid documentId, byte[] content, string fileName = "план.dwg", string? idempotencyKey = null, string? sha256 = null, string? comment = null)
        => client.UploadDocumentVersionAsync(documentId, new MemoryStream(content), fileName, "application/acad", comment, idempotencyKey ?? Guid.NewGuid().ToString("N"), sha256);

    private static async Task<(byte[] Content, DownloadedFile Info)> DownloadAsync(DrawingFlowApiClient client, Guid documentId, Guid versionId)
    {
        using var destination = new MemoryStream();
        var info = await client.DownloadDocumentVersionAsync(documentId, versionId, destination);
        return (destination.ToArray(), info);
    }

    private static Task<DocumentDto> RegisterAsync(DrawingFlowApiClient client, Guid projectId, string number, string title = "Документ", string? discipline = null)
        => client.RegisterDocumentAsync(projectId, new RegisterDocumentRequest(number, title, null, discipline, "Чертёж"));

    private int StoredFileCount() => Directory.Exists(factory.StorageRoot)
        ? Directory.GetFiles(factory.StorageRoot, "*", SearchOption.AllDirectories).Count(f => !f.Contains($"{Path.DirectorySeparatorChar}.tmp{Path.DirectorySeparatorChar}"))
        : 0;

    [Fact]
    public async Task Register_document_with_uniqueness_and_access_rules()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, " АР-01.2 ", "План фундаментов", "КЖ");

        Assert.Equal("АР-01.2", document.DocumentNumber);
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Equal(0, document.VersionCount);
        Assert.Null(document.CurrentVersion);
        Assert.Equal(s.Project.Code, document.ProjectCode);
        Assert.Contains(ProjectPermissions.UploadVersions, document.Permissions);

        var asViewer = await s.Viewer.GetDocumentAsync(document.Id);
        Assert.Equal(new[] { ProjectPermissions.View }, asViewer.Permissions);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetDocumentAsync(document.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Member.GetDocumentAsync(document.Id));

        var conflict = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Conflict, () => RegisterAsync(s.Engineer, s.Project.Id, "ар-01.2"));
        Assert.Equal("conflict", conflict.ErrorCode);
        var validation = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => RegisterAsync(s.Engineer, s.Project.Id, "   "));
        Assert.True(validation.Errors!.ContainsKey("documentNumber"));

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => RegisterAsync(s.Viewer, s.Project.Id, "V-1"));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => RegisterAsync(s.Reviewer, s.Project.Id, "R-1"));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => RegisterAsync(s.Outsider, s.Project.Id, "O-1"));
    }

    [Fact]
    public async Task Versions_are_sequential_immutable_and_current_is_tracked()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "КЖ-1");
        var first = Bytes("Версия 1");
        var second = Bytes("Версия 2 — другое содержимое");

        var v1 = await UploadAsync(s.Engineer, document.Id, first, comment: "Первая выдача", sha256: Sha(first));
        var v2 = await UploadAsync(s.Engineer, document.Id, second, fileName: "план (изм.1).dwg");

        Assert.Equal(1, v1.VersionNumber);
        Assert.Equal(2, v2.VersionNumber);
        Assert.Equal(Sha(first), v1.Sha256Hash);
        Assert.Equal(first.Length, v1.FileSize);
        Assert.Equal("Первая выдача", v1.Comment);
        Assert.Equal(s.Login("engineer").Replace("_", " "), v1.CreatedByName);
        Assert.Equal("application/acad", v1.ContentType);

        var reloaded = await s.Viewer.GetDocumentAsync(document.Id);
        Assert.Equal(DocumentStatus.InProgress, reloaded.Status);
        Assert.Equal(2, reloaded.VersionCount);
        Assert.Equal(v2.Id, reloaded.CurrentVersion!.Id);
        Assert.Equal("план (изм.1).dwg", reloaded.CurrentVersion.OriginalFileName);

        var versions = await s.Viewer.GetDocumentVersionsAsync(document.Id);
        Assert.Equal(new[] { 2, 1 }, versions.Select(v => v.VersionNumber));
        Assert.True(versions[0].IsCurrent);
        Assert.False(versions[1].IsCurrent);

        // Прежняя версия остаётся доступной байт в байт.
        var (downloaded, info) = await DownloadAsync(s.Viewer, document.Id, v1.Id);
        Assert.Equal(first, downloaded);
        Assert.Equal("план.dwg", info.FileName);
        Assert.Equal(Sha(first), info.Sha256Hash);
        Assert.Equal("application/acad", info.ContentType);
    }

    [Fact]
    public async Task Upload_is_idempotent_by_key()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "ИД-1");
        var content = Bytes("повтор");
        var key = Guid.NewGuid().ToString("N");
        var before = StoredFileCount();

        var first = await UploadAsync(s.Engineer, document.Id, content, idempotencyKey: key);
        var retry = await UploadAsync(s.Engineer, document.Id, content, idempotencyKey: key);
        var other = await UploadAsync(s.Engineer, document.Id, content);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, retry.VersionNumber);
        Assert.Equal(2, other.VersionNumber);
        Assert.Equal(2, (await s.Engineer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.Equal(before + 2, StoredFileCount());
    }

    [Fact]
    public async Task Hash_mismatch_is_rejected_without_leaving_files()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "ХШ-1");
        var before = StoredFileCount();

        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.BadRequest, () => UploadAsync(s.Engineer, document.Id, Bytes("данные"), sha256: new string('0', 64)));

        Assert.Equal("validation", ex.ErrorCode);
        Assert.True(ex.Errors!.ContainsKey("sha256Hash"));
        Assert.Equal(0, (await s.Engineer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.Equal(before, StoredFileCount());
        Assert.Empty(Directory.GetFiles(Path.Combine(factory.StorageRoot, ".tmp")));
    }

    [Fact]
    public async Task Concurrent_uploads_receive_unique_sequential_numbers()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "ПАР-1");
        const int count = 6;

        var versions = await Task.WhenAll(Enumerable.Range(1, count).Select(i => UploadAsync(s.Engineer, document.Id, Bytes($"параллельная загрузка {i}"), fileName: $"файл{i}.pdf")));

        Assert.Equal(Enumerable.Range(1, count), versions.Select(v => v.VersionNumber).OrderBy(n => n));
        Assert.Equal(count, versions.Select(v => v.Id).Distinct().Count());

        var reloaded = await s.Engineer.GetDocumentAsync(document.Id);
        Assert.Equal(count, reloaded.VersionCount);
        Assert.Equal(count, reloaded.CurrentVersion!.VersionNumber);

        foreach (var version in versions)
        {
            var (bytes, _) = await DownloadAsync(s.Engineer, document.Id, version.Id);
            Assert.Equal(version.Sha256Hash, Sha(bytes));
        }
    }

    [Fact]
    public async Task Download_is_access_controlled()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "СК-1");
        var version = await UploadAsync(s.Engineer, document.Id, Bytes("секретный чертёж"));

        var (bytes, _) = await DownloadAsync(s.Viewer, document.Id, version.Id);
        Assert.Equal(Bytes("секретный чертёж"), bytes);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => DownloadAsync(s.Outsider, document.Id, version.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => DownloadAsync(s.Member, document.Id, version.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => DownloadAsync(s.Viewer, document.Id, Guid.NewGuid()));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => UploadAsync(s.Viewer, document.Id, Bytes("нельзя")));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => UploadAsync(s.Reviewer, document.Id, Bytes("нельзя")));

        // Версия другого документа недоступна по чужому идентификатору документа.
        var other = await RegisterAsync(s.Engineer, s.Project.Id, "СК-2");
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => DownloadAsync(s.Viewer, other.Id, version.Id));
    }

    [Fact]
    public async Task Oversized_upload_is_rejected()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "БОЛ-1");
        var before = StoredFileCount();

        var ex = await Assert.ThrowsAsync<ApiException>(() => UploadAsync(s.Engineer, document.Id, new byte[ApiFactory.MaxFileSizeBytes + 1]));

        Assert.True(ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, ex.StatusCode.ToString());
        Assert.Equal(0, (await s.Engineer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.Equal(before, StoredFileCount());

        var ok = await UploadAsync(s.Engineer, document.Id, new byte[ApiFactory.MaxFileSizeBytes]);
        Assert.Equal(ApiFactory.MaxFileSizeBytes, ok.FileSize);
    }

    [Fact]
    public async Task Archive_blocks_uploads_until_restore()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "АРХ-1");
        await UploadAsync(s.Engineer, document.Id, Bytes("v1"));

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Engineer.ArchiveDocumentAsync(document.Id));
        var archived = await s.Manager.ArchiveDocumentAsync(document.Id);
        Assert.Equal(DocumentStatus.Archived, archived.Status);

        var blocked = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => UploadAsync(s.Engineer, document.Id, Bytes("v2")));
        Assert.Equal("domain_rule", blocked.ErrorCode);
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.UnprocessableEntity, () => s.Manager.ArchiveDocumentAsync(document.Id));

        var restored = await s.Manager.RestoreDocumentAsync(document.Id);
        Assert.Equal(DocumentStatus.InProgress, restored.Status);
        Assert.Equal(2, (await UploadAsync(s.Engineer, document.Id, Bytes("v2"))).VersionNumber);
    }

    [Fact]
    public async Task Metadata_update_uses_optimistic_concurrency()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "МД-1", "Старое название");

        var updated = await s.Engineer.UpdateDocumentAsync(document.Id, new UpdateDocumentRequest("Новое название", "Описание", "АР", "Схема", document.RowVersion));
        Assert.Equal("Новое название", updated.Title);
        Assert.Equal("АР", updated.Discipline);
        Assert.NotEqual(document.RowVersion, updated.RowVersion);

        var stale = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Conflict, () => s.Engineer.UpdateDocumentAsync(document.Id, new UpdateDocumentRequest("Параллельное изменение", null, null, null, document.RowVersion)));
        Assert.Contains("изменены другим пользователем", stale.Message);

        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.Forbidden, () => s.Viewer.UpdateDocumentAsync(document.Id, new UpdateDocumentRequest("Нельзя", null, null, null, updated.RowVersion)));
        Assert.Equal("Новое название", (await s.Viewer.GetDocumentAsync(document.Id)).Title);
    }

    [Fact]
    public async Task Register_search_filter_sort_and_paging()
    {
        var s = await Scenario.CreateAsync(factory);
        for (var i = 1; i <= 4; i++)
        {
            await RegisterAsync(s.Engineer, s.Project.Id, $"КЖ-{i:00}", $"Конструкции железобетонные {i}", "КЖ");
        }

        await RegisterAsync(s.Engineer, s.Project.Id, "АР-01", "Архитектурные решения", "АР");
        var archived = await RegisterAsync(s.Engineer, s.Project.Id, "ЭОМ-01", "Электрооборудование", "ЭОМ");
        await s.Manager.ArchiveDocumentAsync(archived.Id);

        var all = await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery());
        Assert.Equal(6, all.TotalCount);
        Assert.Equal("АР-01", all.Items[0].DocumentNumber);

        var byNumber = await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery(Search: "кж-0"));
        Assert.Equal(4, byNumber.TotalCount);
        var byTitle = await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery(Search: "АРХИТЕКТУРНЫЕ"));
        Assert.Single(byTitle.Items);

        var byDiscipline = await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery(Discipline: "кж", Sort: DocumentSortField.Number, Descending: true, Page: 2, PageSize: 3));
        Assert.Equal(4, byDiscipline.TotalCount);
        Assert.Single(byDiscipline.Items);
        Assert.Equal("КЖ-01", byDiscipline.Items[0].DocumentNumber);

        var archivedOnly = await s.Viewer.GetDocumentsAsync(s.Project.Id, new DocumentListQuery(Status: DocumentStatus.Archived));
        Assert.Single(archivedOnly.Items);

        Assert.Equal(new[] { "АР", "КЖ", "ЭОМ" }, await s.Viewer.GetDocumentDisciplinesAsync(s.Project.Id));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => s.Outsider.GetDocumentsAsync(s.Project.Id, new DocumentListQuery()));
    }

    [Fact]
    public async Task Recent_documents_respect_project_visibility()
    {
        var a = await Scenario.CreateAsync(factory);
        var b = await Scenario.CreateAsync(factory);
        var visible = await RegisterAsync(a.Engineer, a.Project.Id, "ВИД-1");
        await RegisterAsync(b.Engineer, b.Project.Id, "ЧУЖ-1");

        var asViewer = await a.Viewer.GetRecentDocumentsAsync(a.Organization.Id);
        Assert.Contains(asViewer, d => d.Id == visible.Id);
        Assert.DoesNotContain(asViewer, d => d.DocumentNumber == "ЧУЖ-1");
        Assert.All(asViewer, d => Assert.Equal(new[] { ProjectPermissions.View }, d.Permissions));

        Assert.Empty(await a.Member.GetRecentDocumentsAsync(a.Organization.Id));
        Assert.Contains(await a.OrgAdmin.GetRecentDocumentsAsync(a.Organization.Id), d => d.Id == visible.Id && d.Permissions.Contains(ProjectPermissions.ManageMembers));
        await ApiAssert.ThrowsStatusAsync(HttpStatusCode.NotFound, () => a.Outsider.GetRecentDocumentsAsync(a.Organization.Id));
    }

    [Fact]
    public async Task Missing_storage_object_is_reported_as_storage_error()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "ХР-1");
        var version = await UploadAsync(s.Engineer, document.Id, Bytes("содержимое"), fileName: "файл.pdf");

        var file = Directory.GetFiles(factory.StorageRoot, $"{version.Id:N}*", SearchOption.AllDirectories).Single();
        File.Delete(file);

        var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.ServiceUnavailable, () => DownloadAsync(s.Engineer, document.Id, version.Id));
        Assert.Equal("storage_unavailable", ex.ErrorCode);
        // Метаданные версии при этом остаются доступны.
        Assert.Equal(1, (await s.Engineer.GetDocumentVersionAsync(document.Id, version.Id)).VersionNumber);
    }

    [Fact]
    public async Task Unwritable_storage_fails_safely()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Права POSIX недоступны; сценарий покрывается тестом отсутствующего объекта.
        }

        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "ЗАП-1");
        // Временные файлы пишутся в .tmp; лишаем каталог права записи, имитируя отказ хранилища.
        var tempDirectory = Path.Combine(factory.StorageRoot, ".tmp");
        Directory.CreateDirectory(tempDirectory);
        var originalMode = File.GetUnixFileMode(tempDirectory);
        File.SetUnixFileMode(tempDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var ex = await ApiAssert.ThrowsStatusAsync(HttpStatusCode.ServiceUnavailable, () => UploadAsync(s.Engineer, document.Id, Bytes("нет записи")));
            Assert.Equal("storage_unavailable", ex.ErrorCode);
        }
        finally
        {
            File.SetUnixFileMode(tempDirectory, originalMode);
        }

        Assert.Equal(0, (await s.Engineer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.Equal(1, (await UploadAsync(s.Engineer, document.Id, Bytes("после восстановления"))).VersionNumber);
    }

    [Fact]
    public async Task Document_events_are_audited()
    {
        var s = await Scenario.CreateAsync(factory);
        var document = await RegisterAsync(s.Engineer, s.Project.Id, "АУД-1");
        var version = await UploadAsync(s.Engineer, document.Id, Bytes("аудит"));
        await DownloadAsync(s.Viewer, document.Id, version.Id);

        var audit = await s.Owner.GetAuditAsync(s.Organization.Id, new AuditListQuery(ProjectId: s.Project.Id, PageSize: 100));
        Assert.Contains(audit.Items, e => e.Action == "document.registered" && e.EntityId == document.Id.ToString());
        Assert.Contains(audit.Items, e => e.Action == "document.version_uploaded" && e.EntityId == version.Id.ToString() && e.Details!.Contains("\"versionNumber\": 1"));
        Assert.Contains(audit.Items, e => e.Action == "document.version_downloaded" && e.ActorName == s.Login("viewer").Replace("_", " "));
    }
}
