# Модель данных

СУБД — PostgreSQL 16 (поддерживаются 14+). Схема управляется миграциями EF Core (`src/DrawingFlow.Infrastructure/Persistence/Migrations`).
Все метки времени — `timestamp with time zone` в UTC. Идентификаторы — `uuid` (Guid v7), кроме журнала аудита (`bigint identity`).
Перечисления хранятся строками (`varchar(32)`).

## 1. Реализовано (миграция `InitialSchema`)

### Users
| Колонка | Тип | Примечание |
|---|---|---|
| Id | uuid PK | |
| Login | varchar(64) | исходное написание |
| NormalizedLogin | varchar(64) | верхний регистр, **уникальный индекс** |
| DisplayName | varchar(200) | |
| Email | varchar(254) null | |
| PasswordHash | varchar(512) | Identity V3 (PBKDF2) |
| IsServerAdministrator | boolean | администратор сервера = владелец всех организаций |
| IsActive | boolean | блокировка завершает все сессии |
| CreatedAtUtc, LastLoginAtUtc, PasswordChangedAtUtc | timestamptz | |

### RefreshTokens
| Колонка | Тип | Примечание |
|---|---|---|
| Id | uuid PK | |
| UserId | uuid FK → Users (cascade) | |
| TokenHash | varchar(64) | SHA-256 hex, **уникальный индекс** |
| CreatedAtUtc, ExpiresAtUtc, RevokedAtUtc | timestamptz | индекс (UserId, ExpiresAtUtc) |
| ReplacedByTokenHash | varchar(64) null | цепочка ротации; заполнено → повторное использование = компрометация |
| ClientDescription | varchar(200) null | |

### Organizations
Id PK; Code varchar(50); NormalizedCode varchar(50) **уникальный**; Name varchar(200); IsActive; CreatedAtUtc; CreatedByUserId.

### OrganizationMemberships
Id PK; OrganizationId FK → Organizations (cascade); UserId FK → Users (cascade); Role (`Member|OrganizationAdmin|Owner`); CreatedAtUtc.
**Уникальный индекс (OrganizationId, UserId)**, индекс UserId.

### Projects
Id PK; OrganizationId FK → Organizations (**restrict**); Code varchar(50); NormalizedCode; Name varchar(200); Description varchar(4000) null;
Status (`Active|Archived`); CreatedAtUtc; CreatedByUserId; UpdatedAtUtc.
**Уникальный индекс (OrganizationId, NormalizedCode)**, индекс (OrganizationId, Name).

### ProjectMemberships
Id PK; ProjectId FK → Projects (cascade); UserId FK → Users (cascade); Role (`Viewer|Reviewer|Engineer|ProjectManager`); CreatedAtUtc.
**Уникальный индекс (ProjectId, UserId)**, индекс UserId.

### AuditEvents (только добавление)
| Колонка | Тип |
|---|---|
| Id | bigint identity always PK |
| OccurredAtUtc | timestamptz |
| ActorUserId, ActorAgentId | uuid null |
| ActorName | varchar(200) |
| OrganizationId, ProjectId | uuid null |
| Action | varchar(100) — коды в `Domain/Common/AuditActions.cs` |
| EntityType | varchar(100), EntityId varchar(100) null |
| Details | jsonb null — без секретов |
| ClientAddress | varchar(64) null |

Индексы: (OrganizationId, OccurredAtUtc DESC), (ProjectId, OccurredAtUtc DESC), ActorUserId.

## 2. Реализовано (миграция `AddDocuments`, этап 2)

### Documents
| Колонка | Тип | Примечание |
|---|---|---|
| Id | uuid PK | |
| ProjectId | uuid FK → Projects (restrict) | |
| DocumentNumber | varchar(100) | исходное написание шифра |
| NormalizedDocumentNumber | varchar(100) | верхний регистр, схлопнутые пробелы; **уникальный индекс (ProjectId, NormalizedDocumentNumber)** |
| Title | varchar(300) | |
| Description | varchar(4000) null | |
| Discipline, DocumentType | varchar(100) null | раздел (АР, КЖ…) и тип документа |
| Status | varchar(32) | `Draft|InProgress|InReview|RevisionRequired|Approved|Released|Archived`; переходы — `Domain/Workflow/DocumentWorkflow.cs` |
| CurrentVersionId | uuid null FK → DocumentVersions (restrict) | указатель на текущую версию |
| CreatedAtUtc, UpdatedAtUtc | timestamptz | |
| CreatedByUserId | uuid | |
| xmin | системный столбец | токен оптимистичной блокировки (`RowVersion` в DTO) |

Индексы: (ProjectId, UpdatedAtUtc DESC), (ProjectId, Status), CurrentVersionId.
Идентичность документа не зависит от имени файла и пути. Документы не удаляются — только архивируются.

### DocumentVersions — неизменяемые
| Колонка | Тип | Примечание |
|---|---|---|
| Id | uuid PK | |
| DocumentId | uuid FK → Documents (restrict) | |
| VersionNumber | int | **уникальный (DocumentId, VersionNumber)**; выдаётся в транзакции под `SELECT … FOR UPDATE` строки документа |
| OriginalFileName | varchar(260) | только имя, без пути |
| StorageKey | varchar(200) | внутренний ключ `yyyy/MM/{documentId}/{versionId}[.ext]`, **уникальный**; не совпадает с именем пользователя |
| ContentType | varchar(200) | |
| FileSize | bigint | |
| Sha256Hash | char(64) | индекс; вычисляется сервером при приёме потока |
| CreatedAtUtc | timestamptz | |
| CreatedByUserId, CreatedByAgentId | uuid null | ровно один автор |
| Comment | varchar(2000) null | |
| Status | varchar(32) | `Registered|InReview|Approved|RevisionRequired|Released|Superseded` (этап 4) |
| IdempotencyKey | varchar(100) | **уникальный (DocumentId, IdempotencyKey)** — повтор запроса возвращает ту же версию |

Строки версий не обновляются (кроме `Status` на этапе 4) и не удаляются. Взаимные внешние ключи Documents ↔ DocumentVersions требуют
сохранять версию раньше указателя `CurrentVersionId`; сервис делает это двумя фиксациями внутри одной транзакции.

## 3. Реализовано (миграция `AddAgents`, этап 3)

### AgentInstallations
Id PK; OrganizationId FK → Organizations (restrict); Name varchar(200); MachineName varchar(200) null; AgentVersion varchar(50) null;
CredentialHash char(64) **уникальный** (SHA-256 ключа установки; сам ключ не хранится); CredentialPrefix varchar(8); CreatedAtUtc; CreatedByUserId;
RevokedAtUtc, RevokedByUserId null; LastSeenAtUtc, LastHeartbeatAtUtc null; PendingOperations, FailedOperations, UnresolvedOperations int.

### WatchedFolders
Id PK; AgentInstallationId FK → AgentInstallations (cascade); ProjectId FK → Projects (restrict); Path varchar(1000); NormalizedPath varchar(1000) (**уникальный с AgentInstallationId**, без учёта регистра);
IncludePatterns varchar(1000); ExcludePatterns varchar(1000) null; MatchingRule (`NumberFromFileName|FileNameIsNumber|VersionFileName`); DocumentNumberPattern varchar(500) null;
AutoRegisterUnknown, IsEnabled boolean; CreatedAtUtc; CreatedByUserId; UpdatedAtUtc.

### SyncOperations (серверная проекция очереди агента)
Id PK (задаёт агент = ключ идемпотентности загрузки); AgentInstallationId FK (restrict); OrganizationId; WatchedFolderId FK null (set null); ProjectId FK (restrict);
FilePath varchar(1000); FileName varchar(260); FileSize bigint null; Sha256Hash char(64) null; Status (`Pending|Processing|Completed|Failed|NeedsResolution|Cancelled|Dismissed`);
DetectedAtUtc; UpdatedAtUtc; CompletedAtUtc null; RetryCount; LastError varchar(2000) null; DocumentId, DocumentVersionId null; Candidates jsonb null;
RequestedAction (`None|Retry|UploadToDocument|Dismiss`); RequestedDocumentId, RequestedByUserId, RequestedAtUtc null.
Индексы: (OrganizationId, Status, UpdatedAtUtc DESC), (AgentInstallationId, UpdatedAtUtc DESC), (ProjectId, Status).

Локальная очередь агента (SQLite) описана в `file-sync.md`.

## 4. Реализовано (миграция `AddWorkflow`, этап 4)

### Reviews — неизменяемые решения по версиям
Id PK; DocumentId FK → Documents (restrict); DocumentVersionId FK → DocumentVersions (restrict) — решение всегда относится к конкретной версии;
ReviewerUserId FK → Users (restrict); Decision (`Approved|RevisionRequired`); Comment varchar(2000) null (обязателен при `RevisionRequired`); DecidedAtUtc.
Индексы: (DocumentId, DecidedAtUtc DESC), DocumentVersionId.

### WorkflowTasks
Id PK; ProjectId FK (restrict); DocumentId FK (restrict); DocumentVersionId FK (restrict); AssigneeUserId FK → Users (restrict);
Type (`Review|Rework`); Status (`Open|Completed|Cancelled`); CreatedAtUtc; CreatedByUserId null; DueAtUtc null; CompletedAtUtc, CompletedByUserId null; Comment varchar(2000) null.
Индексы: (AssigneeUserId, Status), (DocumentId, Status), (ProjectId, Status).

Правила (`Domain/Entities/Document.cs`, `Domain/Workflow/DocumentWorkflow.cs`): отправить на проверку можно текущую, ещё не проверявшуюся версию документа «В работе»;
решения принимаются только по версии «На проверке»; выпуск — только согласованной версии; новая версия возвращает документ «В работе», вытесняет непроверенную
предыдущую версию (`Superseded`) и отменяет открытые задачи; согласованные и выпущенные версии сохраняют статус в истории.

## 5. Соглашения

* Все изменения схемы — только миграциями: `dotnet ef migrations add <Name> --project src/DrawingFlow.Infrastructure --startup-project src/DrawingFlow.Server`.
* Применение в production — явной командой `DrawingFlow.Server --migrate` (см. `installation.md`), не автоматически.
* Каскадное удаление допускается только для членств и токенов; документы, версии и аудит не удаляются.
