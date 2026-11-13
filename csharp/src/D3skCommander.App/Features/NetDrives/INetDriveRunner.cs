using D3skCommander.Core.FileSystem;
using D3skCommander.Core.NetDrives;

namespace D3skCommander.App.Features.NetDrives;

/// <summary>The calls that reach the OS. The commands use this seam so tests never mount anything.</summary>
public interface INetDriveRunner
{
    /// <summary>True when a connection can be mapped to a drive letter (Windows).</summary>
    bool SupportsDriveLetter { get; }

    /// <summary>
    /// True when a bare server (<c>\server</c>) can be logged in to, and the login kept by the OS credential store
    /// (Windows).
    /// </summary>
    bool SupportsServerLogin { get; }

    FileResult<string> Connect(Share share, Credentials credentials, string? letter);

    /// <summary>Asks the OS credential store to keep the login for <paramref name="server"/>.</summary>
    FileResult<string> Remember(string server, Credentials credentials);

    FileResult<string> Disconnect(string path);

    IReadOnlyList<ConnectedShare> ListConnected();
}

/// <summary>Runs <see cref="NetDrive"/>: the password goes to the OS tool's standard input or to WNet.</summary>
public sealed class OsNetDriveRunner : INetDriveRunner
{
    public bool SupportsDriveLetter => OperatingSystem.IsWindows();

    public bool SupportsServerLogin => OperatingSystem.IsWindows();

    public FileResult<string> Connect(Share share, Credentials credentials, string? letter) =>
        NetDrive.Connect(share, credentials, letter);

    public FileResult<string> Remember(string server, Credentials credentials) => NetDrive.Remember(server, credentials);

    public FileResult<string> Disconnect(string path) => NetDrive.Disconnect(path);

    public IReadOnlyList<ConnectedShare> ListConnected() => NetDrive.ListConnected();
}
