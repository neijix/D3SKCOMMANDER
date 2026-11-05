namespace D3skCommander.Core.Update;

/// <summary>A final GitHub release: version, notes, web page and the uploaded files.</summary>
public sealed class GitHubRelease
{
    public GitHubRelease(string version, string notes, string page, IReadOnlyList<ReleaseAsset> assets)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentException.ThrowIfNullOrEmpty(page);
        ArgumentNullException.ThrowIfNull(assets);
        Version = version;
        Notes = notes;
        Page = page;
        Assets = Copy(assets);
    }

    public string Version { get; }

    public string Notes { get; }

    public string Page { get; }

    public IReadOnlyList<ReleaseAsset> Assets { get; }

    private static ReleaseAsset[] Copy(IReadOnlyList<ReleaseAsset> assets)
    {
        var copy = new ReleaseAsset[assets.Count];
        for (var i = 0; i < assets.Count; i++)
        {
            var asset = assets[i];
            ArgumentNullException.ThrowIfNull(asset);
            copy[i] = asset;
        }

        return copy;
    }
}
