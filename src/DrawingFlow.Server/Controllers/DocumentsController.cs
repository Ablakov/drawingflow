using DrawingFlow.Application.Documents;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Infrastructure.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace DrawingFlow.Server.Controllers;

[ApiController]
public sealed class DocumentsController(DocumentService documents, IOptions<StorageOptions> storageOptions) : ControllerBase
{
    // ---------- Реестр документов проекта ----------

    [HttpGet("api/projects/{projectId:guid}/documents")]
    public Task<PagedResult<DocumentDto>> List(Guid projectId, [FromQuery] DocumentListQuery query, CancellationToken cancellationToken)
        => documents.ListAsync(projectId, query, cancellationToken);

    [HttpGet("api/projects/{projectId:guid}/documents/disciplines")]
    public Task<IReadOnlyList<string>> Disciplines(Guid projectId, CancellationToken cancellationToken)
        => documents.GetDisciplinesAsync(projectId, cancellationToken);

    /// <summary>Точный поиск документов по номеру и/или имени файла текущей версии (сопоставление файлов агентом и пользователем).</summary>
    [HttpGet("api/projects/{projectId:guid}/documents/lookup")]
    public Task<IReadOnlyList<DocumentDto>> Lookup(Guid projectId, [FromQuery] string? documentNumber, [FromQuery] string? fileName, CancellationToken cancellationToken)
        => documents.LookupAsync(projectId, documentNumber, fileName, cancellationToken);

    [HttpPost("api/projects/{projectId:guid}/documents")]
    public async Task<ActionResult<DocumentDto>> Register(Guid projectId, [FromBody] RegisterDocumentRequest request, CancellationToken cancellationToken)
    {
        var document = await documents.RegisterAsync(projectId, request, cancellationToken);
        return Created($"/api/documents/{document.Id}", document);
    }

    [HttpGet("api/organizations/{organizationId:guid}/documents/recent")]
    public Task<IReadOnlyList<DocumentDto>> Recent(Guid organizationId, [FromQuery] int take = 10, CancellationToken cancellationToken = default)
        => documents.ListRecentAsync(organizationId, take, cancellationToken);

    // ---------- Документ ----------

    [HttpGet("api/documents/{documentId:guid}")]
    public Task<DocumentDto> Get(Guid documentId, CancellationToken cancellationToken)
        => documents.GetAsync(documentId, cancellationToken);

    [HttpPut("api/documents/{documentId:guid}")]
    public Task<DocumentDto> Update(Guid documentId, [FromBody] UpdateDocumentRequest request, CancellationToken cancellationToken)
        => documents.UpdateAsync(documentId, request, cancellationToken);

    [HttpPost("api/documents/{documentId:guid}/archive")]
    public Task<DocumentDto> Archive(Guid documentId, CancellationToken cancellationToken)
        => documents.ArchiveAsync(documentId, cancellationToken);

    [HttpPost("api/documents/{documentId:guid}/restore")]
    public Task<DocumentDto> Restore(Guid documentId, CancellationToken cancellationToken)
        => documents.RestoreAsync(documentId, cancellationToken);

    // ---------- Версии ----------

    [HttpGet("api/documents/{documentId:guid}/versions")]
    public Task<IReadOnlyList<DocumentVersionDto>> ListVersions(Guid documentId, CancellationToken cancellationToken)
        => documents.GetVersionsAsync(documentId, cancellationToken);

    [HttpGet("api/documents/{documentId:guid}/versions/{versionId:guid}")]
    public Task<DocumentVersionDto> GetVersion(Guid documentId, Guid versionId, CancellationToken cancellationToken)
        => documents.GetVersionAsync(documentId, versionId, cancellationToken);

    /// <summary>
    /// Потоковая загрузка новой версии: содержимое файла — тело запроса, параметры — в строке запроса.
    /// Повтор с тем же ключом идемпотентности возвращает 200 и уже созданную версию.
    /// </summary>
    [HttpPost("api/documents/{documentId:guid}/versions")]
    public async Task<ActionResult<DocumentVersionDto>> Upload(Guid documentId, [FromQuery] UploadVersionParameters parameters, CancellationToken cancellationToken)
    {
        var sizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            // Небольшой запас сверх лимита хранилища: превышение лимита должно давать понятную ошибку валидации, а не обрыв соединения.
            sizeFeature.MaxRequestBodySize = storageOptions.Value.MaxFileSizeBytes + 64 * 1024;
        }

        string? mediaType = null;
        if (MediaTypeHeaderValue.TryParse(Request.ContentType, out var parsed) && parsed.MediaType.HasValue)
        {
            mediaType = parsed.MediaType.Value;
        }

        var command = new UploadVersionCommand(parameters.FileName, mediaType, parameters.Comment, parameters.IdempotencyKey, parameters.Sha256Hash, Request.Body);
        var result = await documents.UploadVersionAsync(documentId, command, cancellationToken);
        return result.Created
            ? Created($"/api/documents/{documentId}/versions/{result.Version.Id}", result.Version)
            : Ok(result.Version);
    }

    [HttpGet("api/documents/{documentId:guid}/versions/{versionId:guid}/content")]
    public async Task<IActionResult> Download(Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var content = await documents.OpenVersionContentAsync(documentId, versionId, cancellationToken);
        Response.Headers["X-DrawingFlow-Sha256"] = content.Version.Sha256Hash;
        Response.Headers["X-DrawingFlow-Version"] = content.Version.VersionNumber.ToString();
        return File(content.Content, content.Version.ContentType, content.Version.OriginalFileName, enableRangeProcessing: true);
    }
}
