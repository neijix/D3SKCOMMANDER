using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>
/// Looks for a signed release newer than the running version through <see cref="UpdateService"/>. Never throws for a
/// network, sign-in or keychain problem and never starts a sign-in: the <see cref="UpdateCheck"/> status says it.
/// </summary>
public sealed class UpdateChecker
{
    private readonly UpdateService _service;
    private readonly UpdateEnvironment _environment;

    public UpdateChecker(UpdateService service, UpdateEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(environment);
        _service = service;
        _environment = environment;
    }

    public UpdateEnvironment Environment => _environment;

    /// <summary>
    /// The quiet start-up check runs when the preference is on, the screenshot hook is off, and
    /// <c>D3SK_NO_UPDATE_CHECK</c> is not set (tests and offline runs).
    /// </summary>
    public static bool ShouldCheckAtStart(bool checkUpdates, bool screenshotHookActive, bool disabledByEnvironment) =>
        checkUpdates && !screenshotHookActive && !disabledByEnvironment;

    /// <summary>
    /// The release file name for <paramref name="version"/> on this install (<see cref="AssetChoice.Suffix"/>). A source
    /// build is never updated but still looks for a newer release, so it asks for the portable archive's name.
    /// </summary>
    public static string AssetFor(UpdateEnvironment environment, string version)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrEmpty(version);
        var suffix = AssetChoice.Suffix(environment.Kind, environment.Os, environment.Arch)
            ?? "-" + environment.Os + "-" + environment.Arch + ".tar.gz";
        return "d3skcommander-" + version + suffix;
    }

    public Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default) =>
        _service.CheckAsync(_environment.Version, cancellationToken);
}
