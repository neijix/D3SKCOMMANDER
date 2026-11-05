namespace D3skCommander.Core.Ftp;

/// <summary>
/// A transfer stream that hands the connection back when it is closed. Closing a half-read
/// download or a written upload also ends the data connection on the server side.
/// </summary>
internal sealed class GatedStream : Stream
{
    private readonly Stream _inner;
    private SemaphoreSlim? _gate;

    public GatedStream(Stream inner, SemaphoreSlim gate)
    {
        _inner = inner;
        _gate = gate;
    }

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => _inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                DataStreams.Close(_inner);
            }
            finally
            {
                Release();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await DataStreams.CloseAsync(_inner).ConfigureAwait(false);
        }
        finally
        {
            Release();
        }

        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        var gate = Interlocked.Exchange(ref _gate, null);
        gate?.Release();
    }
}
