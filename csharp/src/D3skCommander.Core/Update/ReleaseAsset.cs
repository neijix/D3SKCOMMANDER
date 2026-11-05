namespace D3skCommander.Core.Update;

/// <summary>One file of a release. <see cref="Sha256"/> is lowercase hex, without the <c>sha256:</c> prefix.</summary>
public sealed class ReleaseAsset
{
    public ReleaseAsset(string name, string url, long size, string? sha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(url);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Name = name;
        Url = url;
        Size = size;
        Sha256 = sha256;
    }

    public string Name { get; }

    public string Url { get; }

    public long Size { get; }

    public string? Sha256 { get; }
}
