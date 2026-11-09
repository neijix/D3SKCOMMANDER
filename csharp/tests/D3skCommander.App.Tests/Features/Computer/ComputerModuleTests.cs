using D3skCommander.App.Features.Computer;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Computer;

public sealed class ComputerModuleTests
{
    [Fact]
    public async Task TheAppProviderListsComputerFromTheSourcesInGroupOrder()
    {
        var home = Path.GetTempPath();
        using var services = TestComposition.Build(configure: s =>
        {
            s.AddSingleton<IComputerSources>(new FakeSources(home));
            s.AddSingleton(new ComputerOptions([Path.GetDirectoryName(home.TrimEnd(Path.DirectorySeparatorChar))!]));
        });
        var provider = services.GetRequiredService<IFileSystemProvider>();

        var rows = new List<Entry>();
        await foreach (var item in provider.ListAsync(ComputerAddress.Text, false, CancellationToken.None))
        {
            Assert.True(item.IsSuccess);
            rows.Add(item.Value);
        }

        Assert.Equal([1, 2, 4], rows.Select(row => row.Group).ToArray());
        Assert.Equal(["X:\\", "Y:\\", home], rows.Select(row => row.Path).ToArray());
    }

    [Fact]
    public void ComputerOptionsDefaultToTheFourRemovablePrefixes()
    {
        using var services = TestComposition.Build();

        Assert.Equal(PanelFileSystemProvider.DefaultPrefixes, services.GetRequiredService<ComputerOptions>().RemovablePrefixes);
    }

    private sealed class FakeSources(string home) : IComputerSources
    {
        public IReadOnlyList<DriveEntry> MountedDrives() => [new DriveEntry("X:\\", "Disk", "ntfs", 1, 2)];

        public IReadOnlyList<ConnectedShare> ConnectedShares() => [new ConnectedShare("Share", "Y:\\")];

        public IReadOnlyList<UserPlace> Places() => [new UserPlace("places.home", home)];

        public IReadOnlyList<HotlistItem> Favorites() => [new HotlistItem("Fav", home)];

        public IReadOnlyList<string> MountPoints() => [];
    }
}
