using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;

namespace D3skCommander.Core.Tests.Providers;

/// <summary>The panel provider over a temp folder: for an ordinary folder it must behave exactly like Local.</summary>
public sealed class PanelProviderFixture : ProviderFixture
{
    private readonly LocalProviderFixture _local = new();

    public PanelProviderFixture() =>
        Provider = Build(new StubComputerSources(), null);

    public override IFileSystemProvider Provider { get; }

    public override string Directory => _local.Directory;

    public override IReadOnlyList<string> FileNames => _local.FileNames;

    public override string SampleFileName => _local.SampleFileName;

    public override string SampleFilePath => _local.SampleFilePath;

    public override byte[] SampleBytes => _local.SampleBytes;

    public override string Combine(string directory, string name) => _local.Combine(directory, name);

    public override void Dispose() => _local.Dispose();

    /// <summary>A panel provider over the real local provider and a Computer provider on <paramref name="sources"/>.</summary>
    public static PanelFileSystemProvider Build(IComputerSources sources, IEnumerable<string>? prefixes)
    {
        var text = Localizer.Load(Language.En, Language.En);
        return new PanelFileSystemProvider(
            new LocalFileSystemProvider(),
            new ComputerFileSystemProvider(sources, text),
            sources,
            prefixes);
    }
}

/// <summary>Fixed answers; counts how often the mount points are read.</summary>
public sealed class StubComputerSources : IComputerSources
{
    public IReadOnlyList<DriveEntry> Drives { get; init; } = [];

    public IReadOnlyList<string> Mounts { get; init; } = [];

    public int MountReads { get; private set; }

    public IReadOnlyList<DriveEntry> MountedDrives() => Drives;

    public IReadOnlyList<ConnectedShare> ConnectedShares() => [];

    public IReadOnlyList<UserPlace> Places() => [];

    public IReadOnlyList<HotlistItem> Favorites() => [];

    public IReadOnlyList<string> MountPoints()
    {
        MountReads++;
        return Mounts;
    }
}
