namespace DrawingFlow.Client;

/// <summary>Обёртка потока только для чтения, сообщающая о количестве прочитанных байт (прогресс загрузки).</summary>
public sealed class ProgressStream(Stream inner, IProgress<long> progress) : Stream
{
    private long _total;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Report(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Report(int read)
    {
        if (read > 0)
        {
            _total += read;
            progress.Report(_total);
        }
    }
}

/// <summary>Результат скачивания версии.</summary>
public sealed record DownloadedFile(string FileName, string ContentType, long Length, string? Sha256Hash);
