using System.Net;
using System.Security.Cryptography;
using System.Text;
using DrawingFlow.Application.Abstractions;
using DrawingFlow.Client;
using DrawingFlow.Contracts.Agents;
using DrawingFlow.Contracts.Documents;
using DrawingFlow.Infrastructure.Options;
using DrawingFlow.Infrastructure.Persistence;
using DrawingFlow.Infrastructure.Security;
using DrawingFlow.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DrawingFlow.IntegrationTests.Api;

/// <summary>Отказоустойчивость: перезапуск сервера под нагрузкой агента, истечение токенов посреди сеанса, большие файлы, параллельные агенты.</summary>
public sealed class ReliabilityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class PastClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static JwtOptions JwtOptionsOf(ApiFactory f) => f.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

    [Fact]
    public async Task Server_restart_with_pending_agent_operation_does_not_duplicate_versions()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var h = await AgentHarness.CreateAsync(factory, s);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ПЗ-1", "Перезапуск сервера"));
        h.WriteFile("ПЗ-1_файл.txt", "до перезапуска сервера");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        await h.Engine.ProcessDueAsync(CancellationToken.None); // наблюдение стабильности
        h.Clock.Advance(TimeSpan.FromSeconds(2));

        // «Перезапуск»: новый хост над той же базой и хранилищем; агент переподключается с тем же ключом и той же очередью.
        await using var restarted = new ApiFactory(factory);
        await h.BindAsync(restarted);
        await h.RunUntilSettledAsync();

        var reloaded = await s.Viewer.GetDocumentAsync(document.Id);
        Assert.Equal(1, reloaded.VersionCount);
        Assert.Equal(SyncOperationStatus.Completed, (await h.SingleOperationAsync())!.Status);

        // Падение агента после загрузки, но до фиксации результата: повтор с тем же ключом идемпотентности не создаёт вторую версию.
        var path = h.WriteFile("ПЗ-1_файл.txt", "изменение перед сбоем агента");
        await h.Engine.ReconcileAsync(CancellationToken.None);
        var operation = (await h.Queue.ListAsync(CancellationToken.None)).Single(o => o.Status == SyncOperationStatus.Pending);
        var bytes = await File.ReadAllBytesAsync(path);
        await h.Api.UploadDocumentVersionAsync(document.Id, new MemoryStream(bytes), operation.FileName, null, "до сбоя", operation.Id.ToString("N"), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal(2, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);

        await h.RunUntilSettledAsync();
        Assert.Equal(2, (await s.Viewer.GetDocumentAsync(document.Id)).VersionCount);
        Assert.All(await h.Queue.ListAsync(CancellationToken.None), o => Assert.Equal(SyncOperationStatus.Completed, o.Status));
    }

    [Fact]
    public async Task Expired_user_access_token_is_refreshed_transparently()
    {
        var client = await factory.LoginAdminAsync();
        var session = client.Sessions.Current!;

        using var scope = factory.Services.CreateScope();
        var admin = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FirstAsync(u => u.Login == ApiFactory.AdminLogin);
        var expiredService = new JwtTokenService(Options.Create(JwtOptionsOf(factory)), new PastClock(DateTime.UtcNow.AddHours(-3)));
        var expired = expiredService.CreateAccessToken(admin);

        // Клиент считает токен действующим (срок в будущем), сервер его отклонит как просроченный.
        await client.Sessions.SetAsync(session with { AccessToken = expired.Value, AccessTokenExpiresAtUtc = DateTime.UtcNow.AddHours(1) });
        var raw = factory.CreateRawClient(expired.Value);
        var rejected = await raw.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Contains("token_expired", await rejected.Content.ReadAsStringAsync());

        var me = await client.GetCurrentUserAsync();
        Assert.Equal(ApiFactory.AdminLogin, me.User.Login);
        Assert.NotEqual(expired.Value, client.Sessions.Current!.AccessToken);
        Assert.NotEqual(session.RefreshToken, client.Sessions.Current.RefreshToken);
    }

    [Fact]
    public async Task Expired_agent_token_is_renewed_with_the_installation_key()
    {
        var s = await Scenario.CreateAsync(factory);
        var registration = await s.OrgAdmin.RegisterAgentAsync(s.Organization.Id, new RegisterAgentRequest("Истекающий"));
        using var scope = factory.Services.CreateScope();
        var agent = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentInstallations.FirstAsync(a => a.Id == registration.Agent.Id);
        var expired = new JwtTokenService(Options.Create(JwtOptionsOf(factory)), new PastClock(DateTime.UtcNow.AddHours(-3))).CreateAgentAccessToken(agent);

        var handler = new AgentTokenHandler(() => new AgentTokenRequest(registration.AgentKey, "WS", "test"), ApiFactory.Json, expired.Value, DateTime.UtcNow.AddHours(1));
        var client = new DrawingFlowApiClient(factory.CreateDefaultClient(handler), new SessionManager(new InMemorySessionStore()));

        var configuration = await client.GetAgentConfigurationAsync();
        Assert.Equal(registration.Agent.Id, configuration.AgentId);
        Assert.Equal(AgentAuthState.Authenticated, handler.State);
        Assert.True(handler.TokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(30));
    }

    [Fact]
    public async Task Large_file_streams_through_upload_and_download_intact()
    {
        var large = new ApiFactory { MaxFileSize = 64L * 1024 * 1024 };
        await ((IAsyncLifetime)large).InitializeAsync();
        try
        {
            var s = await Scenario.CreateAsync(large);
            var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("БФ-1", "Большой файл"));
            var payload = new byte[8 * 1024 * 1024 + 123];
            RandomNumberGenerator.Fill(payload);
            var hash = Convert.ToHexStringLower(SHA256.HashData(payload));

            // Поток без известной длины: передача по частям, без буферизации в памяти клиента.
            var version = await s.Engineer.UploadDocumentVersionAsync(document.Id, new NonSeekableStream(new MemoryStream(payload)), "модель.rvt", "application/octet-stream", null, Guid.NewGuid().ToString("N"), hash);
            Assert.Equal(payload.Length, version.FileSize);
            Assert.Equal(hash, version.Sha256Hash);

            using var destination = new MemoryStream();
            long lastProgress = 0;
            var info = await s.Viewer.DownloadDocumentVersionAsync(document.Id, version.Id, destination, new Progress<long>(p => lastProgress = p));
            Assert.Equal(payload.Length, info.Length);
            Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(destination.ToArray())));
            Assert.True(lastProgress > 0);
        }
        finally
        {
            await ((IAsyncLifetime)large).DisposeAsync();
        }
    }

    [Fact]
    public async Task Two_agents_uploading_to_one_document_get_sequential_versions()
    {
        var s = await Scenario.CreateAsync(factory);
        await using var first = await AgentHarness.CreateAsync(factory, s);
        await using var second = await AgentHarness.CreateAsync(factory, s);
        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ДВА-1", "Два агента"));
        first.WriteFile("ДВА-1_станция1.txt", "вариант станции 1");
        second.WriteFile("ДВА-1_станция2.txt", "вариант станции 2");

        await first.Engine.ReconcileAsync(CancellationToken.None);
        await second.Engine.ReconcileAsync(CancellationToken.None);
        await first.Engine.ProcessDueAsync(CancellationToken.None);
        await second.Engine.ProcessDueAsync(CancellationToken.None);
        first.Clock.Advance(TimeSpan.FromSeconds(2));
        second.Clock.Advance(TimeSpan.FromSeconds(2));
        await Task.WhenAll(first.Engine.ProcessDueAsync(CancellationToken.None), second.Engine.ProcessDueAsync(CancellationToken.None));
        await first.RunUntilSettledAsync();
        await second.RunUntilSettledAsync();

        var versions = await s.Viewer.GetDocumentVersionsAsync(document.Id);
        Assert.Equal(new[] { 2, 1 }, versions.Select(v => v.VersionNumber));
        Assert.Equal(2, versions.Select(v => v.Sha256Hash).Distinct().Count());
        Assert.Equal(SyncOperationStatus.Completed, (await first.SingleOperationAsync())!.Status);
        Assert.Equal(SyncOperationStatus.Completed, (await second.SingleOperationAsync())!.Status);
    }

    [Fact]
    public async Task Readiness_reflects_storage_and_verifier_finds_missing_objects()
    {
        var s = await Scenario.CreateAsync(factory);
        var raw = factory.CreateRawClient();
        Assert.Equal(HttpStatusCode.OK, (await raw.GetAsync("/health/ready")).StatusCode);

        var document = await s.Engineer.RegisterDocumentAsync(s.Project.Id, new RegisterDocumentRequest("ПР-1", "Проверка хранилища"));
        var version = await s.Engineer.UploadDocumentVersionAsync(document.Id, new MemoryStream(Encoding.UTF8.GetBytes("данные")), "а.txt", null, null, Guid.NewGuid().ToString("N"));

        using (var scope = factory.Services.CreateScope())
        {
            var verifier = scope.ServiceProvider.GetRequiredService<DrawingFlow.Infrastructure.Storage.StorageVerifier>();
            var ok = await verifier.VerifyAsync(CancellationToken.None);
            Assert.True(ok.IsConsistent);
            Assert.True(ok.CheckedVersions >= 1);

            File.Delete(Directory.GetFiles(factory.StorageRoot, $"{version.Id:N}*", SearchOption.AllDirectories).Single());
            var broken = await verifier.VerifyAsync(CancellationToken.None);
            var missing = Assert.Single(broken.Missing);
            Assert.Equal(version.Id, missing.VersionId);
            Assert.Equal("ПР-1", missing.DocumentNumber);
        }

        if (!OperatingSystem.IsWindows())
        {
            var temp = Path.Combine(factory.StorageRoot, ".tmp");
            Directory.CreateDirectory(temp);
            var mode = File.GetUnixFileMode(temp);
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                var unhealthy = await raw.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unhealthy.StatusCode);
                Assert.Equal("Unhealthy", await unhealthy.Content.ReadAsStringAsync());
            }
            finally
            {
                File.SetUnixFileMode(temp, mode);
            }

            Assert.Equal(HttpStatusCode.OK, (await raw.GetAsync("/health/ready")).StatusCode);
        }
    }
}
