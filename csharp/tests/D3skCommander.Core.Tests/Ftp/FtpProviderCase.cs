using D3skCommander.Core.Ftp;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Tests.Providers;

namespace D3skCommander.Core.Tests.Ftp;

/// <summary>The FTP provider as one case of the provider contract (<see cref="FtpProviderContractTests"/>).</summary>
public sealed class FtpCase : IProviderCase
{
    public bool ReadOnly => false;

    public ProviderFixture Open() => new FtpProviderFixture();

    public override string ToString() => "FTP";
}

public sealed class FtpProviderFixture : ProviderFixture
{
    private readonly FtpTestSite _host = new();
    private readonly FtpConnection _connection;

    public FtpProviderFixture()
    {
        System.IO.Directory.CreateDirectory(_host.PathOf("folder"));
        SampleBytes = [1, 2, 3, 4, 5];
        FileNames = ["a.txt", "b.txt", "c.txt"];
        foreach (var name in FileNames)
        {
            File.WriteAllBytes(_host.PathOf("folder/" + name), SampleBytes);
        }

        File.WriteAllBytes(_host.PathOf("folder/.hidden"), SampleBytes);
        SampleFileName = FileNames[0];
        SampleFilePath = Combine(Directory, SampleFileName);
        var result = FtpConnection.ConnectAsync(_host.Site, FakeFtpServer.Password, null, CancellationToken.None)
            .GetAwaiter().GetResult();
        _connection = result.Value;
        Provider = new FtpFileSystemProvider(_connection);
    }

    public override IFileSystemProvider Provider { get; }

    public override string Directory => "/folder";

    public override IReadOnlyList<string> FileNames { get; }

    public override string SampleFileName { get; }

    public override string SampleFilePath { get; }

    public override byte[] SampleBytes { get; }

    public override string Combine(string directory, string name) => FtpPaths.Join(directory, name);

    public override void Dispose()
    {
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _host.Dispose();
    }
}
