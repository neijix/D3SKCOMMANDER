using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>The GitHub releases of this repository (<see cref="UpdateMetadata.RepositoryUrl"/>), over HTTPS.</summary>
public sealed class HttpUpdateFeed : IUpdateFeed, IDisposable
{
    private const string SumsName = "SHA256SUMS";

    private readonly HttpAssetSource _http = new(UpdateMetadata.CurrentVersion);

    public Task<UpdateStep<GitHubRelease>> LatestAsync(CancellationToken cancellationToken)
    {
        var url = ReleaseUrls.Latest(UpdateMetadata.RepositoryUrl);
        if (url is null)
        {
            return Task.FromResult(UpdateStep<GitHubRelease>.Fail("no repository address"));
        }

        return _http.ReadLatestAsync(url, cancellationToken);
    }

    public async Task<string?> SumsAsync(GitHubRelease release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        foreach (var asset in release.Assets)
        {
            if (asset.Name == SumsName)
            {
                await using var stream = await _http.OpenAsync(asset.Url, cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return null;
    }

    public void Dispose() => _http.Dispose();
}
