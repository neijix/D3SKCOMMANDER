using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>How a check ended.</summary>
public enum UpdateOutcome
{
    UpToDate,
    Newer,
    Failed,
}

/// <summary>The answer of one check: the release when there is one, and the English detail of a failure.</summary>
public sealed record UpdateCheck(UpdateOutcome Outcome, GitHubRelease? Release, string? Error);

/// <summary>Reads the latest release and compares it to the running version. Never throws for a network failure.</summary>
public sealed class UpdateChecker
{
    private readonly IUpdateFeed _feed;
    private readonly UpdateEnvironment _environment;

    public UpdateChecker(IUpdateFeed feed, UpdateEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(environment);
        _feed = feed;
        _environment = environment;
    }

    public UpdateEnvironment Environment => _environment;

    /// <summary>
    /// The quiet start-up check runs when the preference is on, the screenshot hook is off, and
    /// <c>D3SK_NO_UPDATE_CHECK</c> is not set (tests and offline runs).
    /// </summary>
    public static bool ShouldCheckAtStart(bool checkUpdates, bool screenshotHookActive, bool disabledByEnvironment) =>
        checkUpdates && !screenshotHookActive && !disabledByEnvironment;

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        UpdateStep<GitHubRelease> step;
        try
        {
            step = await _feed.LatestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new UpdateCheck(UpdateOutcome.Failed, null, ex.Message);
        }

        if (!step.IsSuccess || step.Value is null)
        {
            return new UpdateCheck(UpdateOutcome.Failed, null, step.Error);
        }

        var newer = ReleaseVersion.IsNewer(step.Value.Version, _environment.Version);
        return new UpdateCheck(newer ? UpdateOutcome.Newer : UpdateOutcome.UpToDate, step.Value, null);
    }

    /// <summary>The release file for this install, or null (a source build, or a release without one).</summary>
    public ReleaseAsset? PickAsset(GitHubRelease release) =>
        AssetChoice.Pick(release, _environment.Kind, _environment.Os, _environment.Arch);
}
