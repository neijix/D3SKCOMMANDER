using D3skCommander.Core.Update;

namespace D3skCommander.Core.Tests.Update;

internal sealed class BytesSource : IAssetSource
{
    private readonly byte[] _bytes;

    public BytesSource(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        _bytes = bytes;
    }

    public Task<Stream> OpenAsync(string url, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
    }
}
