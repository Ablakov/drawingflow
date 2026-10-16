using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrawingFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MachineName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AgentVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CredentialHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CredentialPrefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PendingOperations = table.Column<int>(type: "integer", nullable: false),
                    FailedOperations = table.Column<int>(type: "integer", nullable: false),
                    UnresolvedOperations = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentInstallations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentInstallations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WatchedFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IncludePatterns = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExcludePatterns = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MatchingRule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DocumentNumberPattern = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AutoRegisterUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchedFolders_AgentInstallations_AgentInstallationId",
                        column: x => x.AgentInstallationId,
                        principalTable: "AgentInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WatchedFolders_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WatchedFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: true),
                    Sha256Hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Candidates = table.Column<string>(type: "jsonb", nullable: true),
                    RequestedAction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncOperations_AgentInstallations_AgentInstallationId",
                        column: x => x.AgentInstallationId,
                        principalTable: "AgentInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncOperations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncOperations_WatchedFolders_WatchedFolderId",
                        column: x => x.WatchedFolderId,
                        principalTable: "WatchedFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentInstallations_CredentialHash",
                table: "AgentInstallations",
                column: "CredentialHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentInstallations_OrganizationId_Name",
                table: "AgentInstallations",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_AgentInstallationId_UpdatedAtUtc",
                table: "SyncOperations",
                columns: new[] { "AgentInstallationId", "UpdatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_OrganizationId_Status_UpdatedAtUtc",
                table: "SyncOperations",
                columns: new[] { "OrganizationId", "Status", "UpdatedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_ProjectId_Status",
                table: "SyncOperations",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_WatchedFolderId",
                table: "SyncOperations",
                column: "WatchedFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolders_AgentInstallationId_NormalizedPath",
                table: "WatchedFolders",
                columns: new[] { "AgentInstallationId", "NormalizedPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolders_ProjectId",
                table: "WatchedFolders",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncOperations");

            migrationBuilder.DropTable(
                name: "WatchedFolders");

            migrationBuilder.DropTable(
                name: "AgentInstallations");
        }
    }
}
