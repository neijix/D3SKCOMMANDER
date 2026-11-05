using System.Runtime.CompilerServices;
using D3skCommander.App.Features.Update;
using D3skCommander.Core.Install;
using D3skCommander.Core.Update;

namespace D3skCommander.App.Tests.Features.Update;

internal static class UpdateTestEnvironment
{
    /// <summary>No test reaches the network: the quiet start-up check of a started app stays off.</summary>
#pragma warning disable CA2255 // A test assembly sets the process-wide opt-out before any test.
    [ModuleInitializer]
    internal static void DisableStartupCheck() =>
        Environment.SetEnvironmentVariable(UpdateCommands.DisableVariable, "1");
#pragma warning restore CA2255
}

internal sealed class FakeFeed : IUpdateFeed
{
    public UpdateStep<GitHubRelease>? Answer { get; set; }

    public Exception? Throw { get; set; }

    public string? Sums { get; set; }

    public int Calls { get; private set; }

    public Task<UpdateStep<GitHubRelease>> LatestAsync(CancellationToken cancellationToken)
    {
        Calls++;
        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(Answer ?? UpdateStep<GitHubRelease>.Fail("none"));
    }

    public Task<string?> SumsAsync(GitHubRelease release, CancellationToken cancellationToken) => Task.FromResult(Sums);
}

internal sealed class BytesSource(byte[] bytes) : IAssetSource
{
    public Task<Stream> OpenAsync(string url, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(bytes));
}

internal sealed class RecordingRestarter : IUpdateRestarter
{
    public Relaunch? Started { get; private set; }

    public void Restart(Relaunch relaunch) => Started = relaunch;
}

internal static class Releases
{
    public static UpdateEnvironment Environment(string version = "0.2.1", InstallKind? kind = null) =>
        new(version, kind ?? new InstallKind.Portable("/opt/d3sk/d3skcommander"), "linux", "x86_64");

    public static GitHubRelease Release(string version, params ReleaseAsset[] assets) =>
        new(version, "Fixes things.", "https://example.invalid/release", assets);

    public static UpdateStep<GitHubRelease> Ok(string version, params ReleaseAsset[] assets) =>
        UpdateStep<GitHubRelease>.Ok(Release(version, assets));
}
