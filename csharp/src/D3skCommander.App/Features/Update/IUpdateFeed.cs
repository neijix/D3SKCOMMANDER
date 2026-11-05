using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>Where the latest release comes from. The app reads GitHub; a test hands back a release.</summary>
public interface IUpdateFeed
{
    /// <summary>The latest final release, or the failure. A network failure may throw <see cref="HttpRequestException"/>.</summary>
    Task<UpdateStep<GitHubRelease>> LatestAsync(CancellationToken cancellationToken);

    /// <summary>The text of the release's <c>SHA256SUMS</c> file, or null when it has none.</summary>
    Task<string?> SumsAsync(GitHubRelease release, CancellationToken cancellationToken);
}
