namespace D3skCommander.Core.Update;

/// <summary>Bytes of one release URL. The app reads HTTPS; a test hands back a stream.</summary>
public interface IAssetSource
{
    Task<Stream> OpenAsync(string url, CancellationToken cancellationToken);
}
