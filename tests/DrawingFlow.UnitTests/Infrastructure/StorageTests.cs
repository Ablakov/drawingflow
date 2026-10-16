using System.Security.Cryptography;
using System.Text;
using DrawingFlow.Application.Storage;
using DrawingFlow.Domain.Exceptions;
using DrawingFlow.Infrastructure.Options;
using DrawingFlow.Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DrawingFlow.UnitTests.Infrastructure;

public sealed class StorageKeysTests
{
    [Fact]
    public void Create_produces_valid_key_with_sanitized_extension()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var key = StorageKeys.Create(documentId, versionId, "План фундаментов.DWG", new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal($"2026/10/{documentId:N}/{versionId:N}.dwg", key);
        Assert.True(StorageKeys.IsValid(key));
    }

    [Theory]
    [InlineData("file", "")]
    [InlineData("file.", "")]
    [InlineData("archive.tar.gz", ".gz")]
    [InlineData("name.ЧЕРТ", "")]
    [InlineData("name.verylongextension", "")]
    [InlineData("a.P D F", "")]
    [InlineData("a.PDF", ".pdf")]
    public void Extension_is_sanitized(string fileName, string expected)
    {
        Assert.Equal(expected, StorageKeys.SanitizeExtension(fileName));
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("2026/../x")]
    [InlineData("/abs/path")]
    [InlineData("2026\\10\\x")]
    [InlineData("")]
    [InlineData("Upper/Case")]
    [InlineData("a//b")]
    public void Invalid_keys_are_rejected(string key)
    {
        Assert.False(StorageKeys.IsValid(key));
    }
}

public sealed class FileSystemStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "drawingflow-unit", Guid.NewGuid().ToString("N"));

    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private FileSystemStorage CreateStorage(long maxSize = 1024) =>
        new(Options.Create(new StorageOptions { RootPath = _root, MaxFileSizeBytes = maxSize }), new TestEnvironment(_root), NullLogger<FileSystemStorage>.Instance);

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Save_computes_size_and_hash_and_places_file_under_root()
    {
        var storage = CreateStorage();
        var content = "Проверка содержимого";
        var key = "2026/10/doc/ver.txt";

        var stored = await storage.SaveAsync(key, Bytes(content), CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetByteCount(content), stored.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))), stored.Sha256Hash);
        Assert.True(await storage.ExistsAsync(key, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(_root, "2026", "10", "doc", "ver.txt")));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".tmp")));

        await using var read = await storage.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(read, Encoding.UTF8);
        Assert.Equal(content, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Oversized_file_is_rejected_and_leaves_nothing_behind()
    {
        var storage = CreateStorage(maxSize: 16);
        var ex = await Assert.ThrowsAsync<ValidationException>(() => storage.SaveAsync("2026/10/doc/big.bin", new MemoryStream(new byte[17]), CancellationToken.None));

        Assert.Contains("превышает", ex.Message);
        Assert.False(await storage.ExistsAsync("2026/10/doc/big.bin", CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".tmp")));
    }

    [Fact]
    public async Task Objects_are_immutable()
    {
        var storage = CreateStorage();
        await storage.SaveAsync("2026/10/doc/v1", Bytes("a"), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync("2026/10/doc/v1", Bytes("b"), CancellationToken.None));
    }

    [Fact]
    public async Task Path_traversal_keys_are_rejected()
    {
        var storage = CreateStorage();
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync("../outside", Bytes("x"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("2026/../../x", CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "outside")));
    }

    [Fact]
    public async Task Missing_object_is_a_storage_error_and_delete_is_idempotent()
    {
        var storage = CreateStorage();
        var ex = await Assert.ThrowsAsync<StorageException>(() => storage.OpenReadAsync("2026/10/doc/missing", CancellationToken.None));
        Assert.Equal("storage_unavailable", ex.ErrorCode);
        await storage.DeleteAsync("2026/10/doc/missing", CancellationToken.None);
    }

    [Fact]
    public void Format_size_is_human_readable()
    {
        Assert.Equal("1 ГБ", FileSystemStorage.FormatSize(1024L * 1024 * 1024));
        Assert.Equal("1,5 МБ", FileSystemStorage.FormatSize(1024 * 1024 + 512 * 1024));
        Assert.Equal("10 Б", FileSystemStorage.FormatSize(10));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
