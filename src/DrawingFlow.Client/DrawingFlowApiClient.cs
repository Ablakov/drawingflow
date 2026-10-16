using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DrawingFlow.Contracts;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Audit;
using DrawingFlow.Contracts.Auth;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Contracts.Organizations;
using DrawingFlow.Contracts.Projects;
using DrawingFlow.Contracts.Users;
using DrawingFlow.Contracts.Workflow;

namespace DrawingFlow.Client;

/// <summary>Типизированный клиент API DrawingFlow. Используется Desktop и Agent.</summary>
public sealed class DrawingFlowApiClient(HttpClient http, SessionManager sessions)
{
    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public Uri? BaseAddress => http.BaseAddress;

    /// <summary>Тайм-аут обычных запросов. Загрузка и скачивание файлов ограничиваются только токеном отмены вызывающей стороны.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(100);

    public SessionManager Sessions => sessions;

    // ---------- Сервер ----------

    public Task<ServerInfoDto> GetServerInfoAsync(CancellationToken cancellationToken = default)
        => GetAsync<ServerInfoDto>("api/server/info", anonymous: true, cancellationToken);

    // ---------- Аутентификация ----------

    public async Task<AuthSession> LoginAsync(string login, string password, string? clientDescription, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<LoginRequest, AuthResponse>("api/auth/login", new LoginRequest(login, password, clientDescription), anonymous: true, cancellationToken);
        var session = AuthSession.From(response);
        await sessions.SetAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var session = sessions.Current;
        if (session is not null)
        {
            try
            {
                await PostAsync("api/auth/logout", new LogoutRequest(session.RefreshToken), anonymous: true, cancellationToken);
            }
            catch (ApiConnectionException)
            {
                // Локальный выход выполняем даже без связи с сервером.
            }
        }

        await sessions.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthSession> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var response = await PostAsync<ChangePasswordRequest, AuthResponse>("api/auth/password", new ChangePasswordRequest(currentPassword, newPassword), anonymous: false, cancellationToken);
        var session = AuthSession.From(response);
        await sessions.SetAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    // ---------- Пользователи ----------

    public Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
        => GetAsync<CurrentUserDto>("api/users/me", anonymous: false, cancellationToken);

    public Task<PagedResult<UserDto>> GetUsersAsync(string? search, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
        => GetAsync<PagedResult<UserDto>>(WithQuery("api/users", ("search", search), ("page", page), ("pageSize", pageSize)), anonymous: false, cancellationToken);

    public Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
        => PostAsync<CreateUserRequest, UserDto>("api/users", request, anonymous: false, cancellationToken);

    public Task<UserDto> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
        => PutAsync<UpdateUserRequest, UserDto>($"api/users/{userId}", request, cancellationToken);

    // ---------- Организации ----------

    public Task<IReadOnlyList<OrganizationDto>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<OrganizationDto>>("api/organizations", anonymous: false, cancellationToken);

    public Task<OrganizationDto> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => GetAsync<OrganizationDto>($"api/organizations/{organizationId}", anonymous: false, cancellationToken);

    public Task<OrganizationDto> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<CreateOrganizationRequest, OrganizationDto>("api/organizations", request, anonymous: false, cancellationToken);

    public Task<OrganizationDto> UpdateOrganizationAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
        => PutAsync<UpdateOrganizationRequest, OrganizationDto>($"api/organizations/{organizationId}", request, cancellationToken);

    public Task<IReadOnlyList<OrganizationMemberDto>> GetOrganizationMembersAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<OrganizationMemberDto>>($"api/organizations/{organizationId}/members", anonymous: false, cancellationToken);

    public Task<OrganizationMemberDto> AddOrganizationMemberAsync(Guid organizationId, AddOrganizationMemberRequest request, CancellationToken cancellationToken = default)
        => PostAsync<AddOrganizationMemberRequest, OrganizationMemberDto>($"api/organizations/{organizationId}/members", request, anonymous: false, cancellationToken);

    public Task<OrganizationMemberDto> UpdateOrganizationMemberAsync(Guid organizationId, Guid userId, UpdateOrganizationMemberRequest request, CancellationToken cancellationToken = default)
        => PutAsync<UpdateOrganizationMemberRequest, OrganizationMemberDto>($"api/organizations/{organizationId}/members/{userId}", request, cancellationToken);

    public Task RemoveOrganizationMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
        => DeleteAsync($"api/organizations/{organizationId}/members/{userId}", cancellationToken);

    public Task<PagedResult<AuditEventDto>> GetAuditAsync(Guid organizationId, AuditListQuery query, CancellationToken cancellationToken = default)
        => GetAsync<PagedResult<AuditEventDto>>(
            WithQuery($"api/organizations/{organizationId}/audit",
                ("projectId", query.ProjectId),
                ("action", query.Action),
                ("fromUtc", query.FromUtc?.ToString("O")),
                ("toUtc", query.ToUtc?.ToString("O")),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            anonymous: false, cancellationToken);

    // ---------- Проекты ----------

    public Task<PagedResult<ProjectDto>> GetProjectsAsync(Guid organizationId, ProjectListQuery query, CancellationToken cancellationToken = default)
        => GetAsync<PagedResult<ProjectDto>>(
            WithQuery($"api/organizations/{organizationId}/projects",
                ("search", query.Search),
                ("status", query.Status?.ToString()),
                ("sort", query.Sort.ToString()),
                ("descending", query.Descending),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            anonymous: false, cancellationToken);

    public Task<ProjectDto> CreateProjectAsync(Guid organizationId, CreateProjectRequest request, CancellationToken cancellationToken = default)
        => PostAsync<CreateProjectRequest, ProjectDto>($"api/organizations/{organizationId}/projects", request, anonymous: false, cancellationToken);

    public Task<ProjectDto> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => GetAsync<ProjectDto>($"api/projects/{projectId}", anonymous: false, cancellationToken);

    public Task<ProjectDto> UpdateProjectAsync(Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default)
        => PutAsync<UpdateProjectRequest, ProjectDto>($"api/projects/{projectId}", request, cancellationToken);

    public Task<IReadOnlyList<ProjectMemberDto>> GetProjectMembersAsync(Guid projectId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<ProjectMemberDto>>($"api/projects/{projectId}/members", anonymous: false, cancellationToken);

    public Task<ProjectMemberDto> AddProjectMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken = default)
        => PostAsync<AddProjectMemberRequest, ProjectMemberDto>($"api/projects/{projectId}/members", request, anonymous: false, cancellationToken);

    public Task<ProjectMemberDto> UpdateProjectMemberAsync(Guid projectId, Guid userId, UpdateProjectMemberRequest request, CancellationToken cancellationToken = default)
        => PutAsync<UpdateProjectMemberRequest, ProjectMemberDto>($"api/projects/{projectId}/members/{userId}", request, cancellationToken);

    public Task RemoveProjectMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
        => DeleteAsync($"api/projects/{projectId}/members/{userId}", cancellationToken);

    // ---------- Документы ----------

    public Task<PagedResult<DocumentDto>> GetDocumentsAsync(Guid projectId, DocumentListQuery query, CancellationToken cancellationToken = default)
        => GetAsync<PagedResult<DocumentDto>>(
            WithQuery($"api/projects/{projectId}/documents",
                ("search", query.Search),
                ("status", query.Status?.ToString()),
                ("discipline", query.Discipline),
                ("sort", query.Sort.ToString()),
                ("descending", query.Descending),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            anonymous: false, cancellationToken);

    public Task<IReadOnlyList<string>> GetDocumentDisciplinesAsync(Guid projectId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<string>>($"api/projects/{projectId}/documents/disciplines", anonymous: false, cancellationToken);

    public Task<IReadOnlyList<DocumentDto>> GetRecentDocumentsAsync(Guid organizationId, int take = 10, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<DocumentDto>>(WithQuery($"api/organizations/{organizationId}/documents/recent", ("take", take)), anonymous: false, cancellationToken);

    public Task<DocumentDto> RegisterDocumentAsync(Guid projectId, RegisterDocumentRequest request, CancellationToken cancellationToken = default)
        => PostAsync<RegisterDocumentRequest, DocumentDto>($"api/projects/{projectId}/documents", request, anonymous: false, cancellationToken);

    public Task<DocumentDto> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        => GetAsync<DocumentDto>($"api/documents/{documentId}", anonymous: false, cancellationToken);

    public Task<DocumentDto> UpdateDocumentAsync(Guid documentId, UpdateDocumentRequest request, CancellationToken cancellationToken = default)
        => PutAsync<UpdateDocumentRequest, DocumentDto>($"api/documents/{documentId}", request, cancellationToken);

    public Task<DocumentDto> ArchiveDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        => PostAsync<DocumentDto>($"api/documents/{documentId}/archive", cancellationToken);

    public Task<DocumentDto> RestoreDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
        => PostAsync<DocumentDto>($"api/documents/{documentId}/restore", cancellationToken);

    public Task<IReadOnlyList<DocumentVersionDto>> GetDocumentVersionsAsync(Guid documentId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<DocumentVersionDto>>($"api/documents/{documentId}/versions", anonymous: false, cancellationToken);

    public Task<DocumentVersionDto> GetDocumentVersionAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
        => GetAsync<DocumentVersionDto>($"api/documents/{documentId}/versions/{versionId}", anonymous: false, cancellationToken);

    /// <summary>
    /// Потоковая загрузка версии. Ключ идемпотентности должен сохраняться между повторами одной и той же загрузки.
    /// Если указана контрольная сумма, сервер отклонит файл при несовпадении.
    /// </summary>
    public async Task<DocumentVersionDto> UploadDocumentVersionAsync(
        Guid documentId,
        Stream content,
        string fileName,
        string? contentType,
        string? comment,
        string idempotencyKey,
        string? sha256Hash = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var path = WithQuery($"api/documents/{documentId}/versions",
            ("fileName", fileName),
            ("idempotencyKey", idempotencyKey),
            ("comment", comment),
            ("sha256Hash", sha256Hash));
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        var body = progress is null ? content : new ProgressStream(content, progress);
        var streamContent = new StreamContent(body, 128 * 1024);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        if (content.CanSeek)
        {
            streamContent.Headers.ContentLength = content.Length - content.Position;
        }

        request.Content = streamContent;
        using var response = await SendRawAsync(request, anonymous: false, cancellationToken, applyTimeout: false).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<DocumentVersionDto>(Json, cancellationToken).ConfigureAwait(false)
            ?? throw new ApiException(response.StatusCode, null, "Пустой ответ сервера.");
    }

    /// <summary>Скачивает содержимое версии в поток назначения.</summary>
    public async Task<DownloadedFile> DownloadDocumentVersionAsync(Guid documentId, Guid versionId, Stream destination, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/documents/{documentId}/versions/{versionId}/content");
        using var response = await SendRawAsync(request, anonymous: false, cancellationToken, applyTimeout: false, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            progress?.Report(total);
        }

        var sha256 = response.Headers.TryGetValues("X-DrawingFlow-Sha256", out var values) ? values.FirstOrDefault() : null;
        return new DownloadedFile(fileName ?? "file", response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", total, sha256);
    }

    public Task<IReadOnlyList<DocumentDto>> LookupDocumentsAsync(Guid projectId, string? documentNumber, string? fileName, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<DocumentDto>>(WithQuery($"api/projects/{projectId}/documents/lookup", ("documentNumber", documentNumber), ("fileName", fileName)), anonymous: false, cancellationToken);

    // ---------- Согласование ----------

    public Task<DocumentWorkflowDto> GetDocumentWorkflowAsync(Guid documentId, CancellationToken cancellationToken = default)
        => GetAsync<DocumentWorkflowDto>($"api/documents/{documentId}/workflow", anonymous: false, cancellationToken);

    public Task<IReadOnlyList<ReviewerCandidateDto>> GetReviewerCandidatesAsync(Guid projectId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<ReviewerCandidateDto>>($"api/projects/{projectId}/reviewers", anonymous: false, cancellationToken);

    public Task<DocumentWorkflowDto> SubmitForReviewAsync(Guid documentId, SubmitForReviewRequest request, CancellationToken cancellationToken = default)
        => PostAsync<SubmitForReviewRequest, DocumentWorkflowDto>($"api/documents/{documentId}/workflow/submit", request, anonymous: false, cancellationToken);

    public Task<DocumentWorkflowDto> DecideReviewAsync(Guid documentId, ReviewDecisionRequest request, CancellationToken cancellationToken = default)
        => PostAsync<ReviewDecisionRequest, DocumentWorkflowDto>($"api/documents/{documentId}/workflow/decide", request, anonymous: false, cancellationToken);

    public Task<DocumentWorkflowDto> ReleaseDocumentAsync(Guid documentId, ReleaseRequest request, CancellationToken cancellationToken = default)
        => PostAsync<ReleaseRequest, DocumentWorkflowDto>($"api/documents/{documentId}/workflow/release", request, anonymous: false, cancellationToken);

    public Task<PagedResult<WorkflowTaskDto>> GetMyTasksAsync(Guid organizationId, WorkflowTaskListQuery query, CancellationToken cancellationToken = default)
        => GetAsync<PagedResult<WorkflowTaskDto>>(
            WithQuery($"api/organizations/{organizationId}/tasks",
                ("status", query.Status?.ToString()), ("type", query.Type?.ToString()), ("projectId", query.ProjectId), ("page", query.Page), ("pageSize", query.PageSize)),
            anonymous: false, cancellationToken);

    public Task<PagedResult<WorkflowTaskDto>> GetProjectTasksAsync(Guid projectId, WorkflowTaskListQuery query, CancellationToken cancellationToken = default)
        => GetAsync<PagedResult<WorkflowTaskDto>>(
            WithQuery($"api/projects/{projectId}/tasks",
                ("status", query.Status?.ToString()), ("type", query.Type?.ToString()), ("page", query.Page), ("pageSize", query.PageSize)),
            anonymous: false, cancellationToken);

    public Task<WorkflowTaskDto> CancelWorkflowTaskAsync(Guid taskId, CancelTaskRequest request, CancellationToken cancellationToken = default)
        => PostAsync<CancelTaskRequest, WorkflowTaskDto>($"api/workflow-tasks/{taskId}/cancel", request, anonymous: false, cancellationToken);

    public Task<WorkflowSummaryDto> GetWorkflowSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => GetAsync<WorkflowSummaryDto>($"api/organizations/{organizationId}/workflow/summary", anonymous: false, cancellationToken);

    // ---------- Агенты: администрирование ----------

    public Task<IReadOnlyList<AgentDto>> GetAgentsAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<AgentDto>>($"api/organizations/{organizationId}/agents", anonymous: false, cancellationToken);

    public Task<AgentRegistrationResultDto> RegisterAgentAsync(Guid organizationId, RegisterAgentRequest request, CancellationToken cancellationToken = default)
        => PostAsync<RegisterAgentRequest, AgentRegistrationResultDto>($"api/organizations/{organizationId}/agents", request, anonymous: false, cancellationToken);

    public Task<AgentDto> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
        => PostAsync<AgentDto>($"api/agents/{agentId}/revoke", cancellationToken);

    public Task<SyncSummaryDto> GetSyncSummaryAsync(Guid organizationId, CancellationToken cancellationToken = default)
        => GetAsync<SyncSummaryDto>($"api/organizations/{organizationId}/sync-summary", anonymous: false, cancellationToken);

    public Task<IReadOnlyList<WatchedFolderDto>> GetWatchedFoldersAsync(Guid agentId, CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<WatchedFolderDto>>($"api/agents/{agentId}/folders", anonymous: false, cancellationToken);

    public Task<WatchedFolderDto> CreateWatchedFolderAsync(Guid agentId, SaveWatchedFolderRequest request, CancellationToken cancellationToken = default)
        => PostAsync<SaveWatchedFolderRequest, WatchedFolderDto>($"api/agents/{agentId}/folders", request, anonymous: false, cancellationToken);

    public Task<WatchedFolderDto> UpdateWatchedFolderAsync(Guid folderId, SaveWatchedFolderRequest request, CancellationToken cancellationToken = default)
        => PutAsync<SaveWatchedFolderRequest, WatchedFolderDto>($"api/watched-folders/{folderId}", request, cancellationToken);

    public Task DeleteWatchedFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
        => DeleteAsync($"api/watched-folders/{folderId}", cancellationToken);

    public Task<PagedResult<SyncOperationDto>> GetSyncOperationsAsync(Guid organizationId, SyncOperationListQuery query, CancellationToken cancellationToken = default)
        => GetAsync<PagedResult<SyncOperationDto>>(
            WithQuery($"api/organizations/{organizationId}/sync-operations",
                ("agentId", query.AgentId),
                ("projectId", query.ProjectId),
                ("status", query.Status?.ToString()),
                ("activeOnly", query.ActiveOnly),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            anonymous: false, cancellationToken);

    public Task<SyncOperationDto> ResolveSyncOperationAsync(Guid operationId, ResolveSyncOperationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<ResolveSyncOperationRequest, SyncOperationDto>($"api/sync-operations/{operationId}/resolve", request, anonymous: false, cancellationToken);

    public Task<SyncOperationDto> RetrySyncOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
        => PostAsync<SyncOperationDto>($"api/sync-operations/{operationId}/retry", cancellationToken);

    public Task<SyncOperationDto> DismissSyncOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
        => PostAsync<SyncOperationDto>($"api/sync-operations/{operationId}/dismiss", cancellationToken);

    // ---------- Агент: служебные вызовы (токен выставляет AgentTokenHandler) ----------

    public Task<AgentTokenResponse> ExchangeAgentKeyAsync(AgentTokenRequest request, CancellationToken cancellationToken = default)
        => PostAsync<AgentTokenRequest, AgentTokenResponse>("api/agents/token", request, anonymous: true, cancellationToken);

    public Task<AgentConfigurationDto> GetAgentConfigurationAsync(CancellationToken cancellationToken = default)
        => GetAsync<AgentConfigurationDto>("api/agent/configuration", anonymous: false, cancellationToken);

    public Task SendAgentHeartbeatAsync(AgentHeartbeatRequest request, CancellationToken cancellationToken = default)
        => PostAsync("api/agent/heartbeat", request, anonymous: false, cancellationToken);

    public Task<IReadOnlyList<AgentActionDto>> ReportSyncOperationsAsync(IReadOnlyList<ReportSyncOperationRequest> reports, CancellationToken cancellationToken = default)
        => PutAsync<IReadOnlyList<ReportSyncOperationRequest>, IReadOnlyList<AgentActionDto>>("api/agent/operations", reports, cancellationToken);

    public Task<IReadOnlyList<AgentActionDto>> GetAgentActionsAsync(CancellationToken cancellationToken = default)
        => GetAsync<IReadOnlyList<AgentActionDto>>("api/agent/operations/actions", anonymous: false, cancellationToken);

    // ---------- Внутренние помощники ----------

    private async Task<TResponse> PostAsync<TResponse>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent([]) };
        return await SendAsync<TResponse>(request, anonymous: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> GetAsync<T>(string path, bool anonymous, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync<T>(request, anonymous, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, bool anonymous, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        return await SendAsync<TResponse>(request, anonymous, cancellationToken).ConfigureAwait(false);
    }

    private async Task PostAsync<TRequest>(string path, TRequest body, bool anonymous, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: Json) };
        using var response = await SendRawAsync(request, anonymous, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body, options: Json) };
        return await SendAsync<TResponse>(request, anonymous: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        using var response = await SendRawAsync(request, anonymous: false, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, bool anonymous, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(request, anonymous, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        var result = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
        return result ?? throw new ApiException(response.StatusCode, null, "Пустой ответ сервера.");
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpRequestMessage request,
        bool anonymous,
        CancellationToken cancellationToken,
        bool applyTimeout = true,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        if (anonymous)
        {
            request.Options.Set(AuthenticatedHttpHandler.AnonymousOption, true);
        }

        using var timeoutSource = applyTimeout ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        if (timeoutSource is not null)
        {
            timeoutSource.CancelAfter(RequestTimeout);
        }

        var token = timeoutSource?.Token ?? cancellationToken;
        try
        {
            return await http.SendAsync(request, completion, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw ApiConnectionException.From(ex, http.BaseAddress);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiConnectionException("Сервер не ответил за отведённое время.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiProblem? problem = null;
        string? raw = null;
        if (response.Content.Headers.ContentLength != 0)
        {
            raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw) && (response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                try
                {
                    problem = JsonSerializer.Deserialize<ApiProblem>(raw, Json);
                }
                catch (JsonException)
                {
                    problem = null;
                }
            }
        }

        throw new ApiException(response.StatusCode, problem, raw);
    }

    private static string WithQuery(string path, params (string Key, object? Value)[] parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (value is null)
            {
                continue;
            }

            var text = value switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(text)}");
        }

        return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
    }
}
