using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;

namespace D3skCommander.App.Features.NetDrives;

/// <summary>The calls that reach the OS. The commands use this seam so tests never mount anything.</summary>
public interface INetDriveRunner
{
    /// <summary>True when a connection can be mapped to a drive letter (Windows).</summary>
    bool SupportsDriveLetter { get; }

    FileResult<string> Connect(Share share, Credentials credentials, string? letter);

    FileResult<string> Disconnect(string path);

    IReadOnlyList<ConnectedShare> ListConnected();
}

/// <summary>Runs <see cref="NetDrive"/>: the password goes to the OS tool's standard input or to WNet.</summary>
public sealed class OsNetDriveRunner : INetDriveRunner
{
    public bool SupportsDriveLetter => OperatingSystem.IsWindows();

    public FileResult<string> Connect(Share share, Credentials credentials, string? letter) =>
        NetDrive.Connect(share, credentials, letter);

    public FileResult<string> Disconnect(string path) => NetDrive.Disconnect(path);

    public IReadOnlyList<ConnectedShare> ListConnected() => NetDrive.ListConnected();
}
