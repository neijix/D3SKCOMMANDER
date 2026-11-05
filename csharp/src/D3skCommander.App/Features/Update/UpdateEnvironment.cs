using System.Runtime.InteropServices;
using D3skCommander.Core.Install;
using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update;

/// <summary>What the update decision needs to know about this run: version, install kind, OS and CPU names.</summary>
public sealed record UpdateEnvironment(string Version, InstallKind Kind, string Os, string Arch)
{
    /// <summary>This process. The names are the ones the release files use (<c>linux</c>, <c>x86_64</c>).</summary>
    public static UpdateEnvironment Current()
    {
        var facts = InstallFacts.Current();
        return new UpdateEnvironment(
            UpdateMetadata.CurrentVersion,
            InstallDetection.Detect(facts),
            facts.Os,
            RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64");
    }
}
