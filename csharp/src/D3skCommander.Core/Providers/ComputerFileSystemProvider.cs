using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Providers;

/// <summary>
/// The Computer view: one read-only folder at <see cref="ComputerAddress.Text"/> whose rows are the mounted
/// drives, the network drives, the user places and the favorites (<see cref="Entry.Group"/> 1 to 4). Every
/// listing reads <see cref="IComputerSources"/> again; a source that fails is left out of it.
/// </summary>
public sealed class ComputerFileSystemProvider : FileSystemProvider
{
    private static readonly string[] NetworkFormats = ["cifs", "smb3", "smbfs", "nfs", "nfs4", "afpfs", "webdav", "davfs", "fuse.sshfs"];

    private readonly IComputerSources _sources;
    private readonly ILocalizer _text;
    private readonly bool _windows;

    /// <param name="windows">Case-insensitive path matching and <c>Z:</c> drive roots; default: the current OS.</param>
    public ComputerFileSystemProvider(IComputerSources sources, ILocalizer text, bool? windows = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(text);
        _sources = sources;
        _text = text;
        _windows = windows ?? OperatingSystem.IsWindows();
    }

    public override bool IsReadOnly => true;

    /// <summary>The drives group's paths: what the all-drives scan covers (network drives left out).</summary>
    public IReadOnlyList<string> LocalDrivePaths() => Build().Where(entry => entry.Group == 1).Select(entry => entry.Path).ToList();

    /// <summary>
    /// What disk usage scans for the cursor row: the drive or network drive it names, else every local drive.
    /// </summary>
    public ComputerScanTarget ScanTarget(Entry? cursor) =>
        cursor is { IsParent: false, Group: 1 or 2 } && !string.IsNullOrEmpty(cursor.Path)
            ? new ComputerScanTarget(cursor.Path, [])
            : new ComputerScanTarget(null, LocalDrivePaths());

    protected override async IAsyncEnumerable<FileResult<Entry>> ListCore(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        if (!ComputerAddress.IsAddress(directory))
        {
            yield return FileResult<Entry>.Fail(OperationError.NotFound(directory));
            yield break;
        }

        foreach (var entry in Build())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
                yield break;
            }

            yield return FileResult<Entry>.Ok(entry);
        }
    }

    protected override Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken)
    {
        if (!ComputerAddress.IsAddress(path))
        {
            return Task.FromResult(FileResult<Entry>.Fail(OperationError.NotFound(path)));
        }

        var self = new Entry(_text.Get("computer.title"), ComputerAddress.Text, true, false, false, 0, null, false, true, "");
        return Task.FromResult(FileResult<Entry>.Ok(self));
    }

    protected override Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<Stream>.Fail(ComputerAddress.IsAddress(path)
            ? OperationError.InvalidData("Computer is a folder, not a file", path)
            : OperationError.NotFound(path)));

    protected override Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<Stream>.Fail(ReadOnly(path)));

    protected override Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileStatus.Fail(ReadOnly(path)));

    protected override Task<FileResult<string>> RenameCore(string path, string newName, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<string>.Fail(ReadOnly(path)));

    protected override Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileStatus.Fail(ReadOnly(path)));

    private static OperationError ReadOnly(string path) => OperationError.Unsupported(ReadOnlyMessage, path);

    private static bool IsNetworkFormat(string format) =>
        NetworkFormats.Contains(format, StringComparer.OrdinalIgnoreCase);

    private static T[] Read<T>(Func<IReadOnlyList<T>> source)
    {
        try
        {
            return [.. source()];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private List<Entry> Build()
    {
        var rows = new List<Entry>();
        var drives = Read(_sources.MountedDrives);
        var shares = Read(_sources.ConnectedShares);
        var network = new List<Entry>();
        var sharePaths = shares.Select(share => Normalize(share.Path)).ToList();

        foreach (var drive in drives)
        {
            var isShare = sharePaths.Contains(Normalize(drive.Path), PathComparer());
            if (isShare || IsNetworkFormat(drive.Format))
            {
                var label = shares.FirstOrDefault(share => PathComparer().Equals(Normalize(share.Path), Normalize(drive.Path)))?.Label;
                var shown = isShare && !string.IsNullOrEmpty(label) ? drive with { Label = label } : drive;
                network.Add(DriveRow(shown, 2));
            }
            else
            {
                rows.Add(DriveRow(drive, 1));
            }
        }

        foreach (var share in shares)
        {
            if (drives.Any(drive => PathComparer().Equals(Normalize(drive.Path), Normalize(share.Path))))
            {
                continue;
            }

            network.Add(DriveRow(new DriveEntry(SharePath(share.Path), share.Label, "", 0, 0), 2));
        }

        // Home lists drives and favorites only: the user places (Desktop, Documents...) stay in the drive menu.
        rows.AddRange(network);
        foreach (var favorite in Read(_sources.Favorites))
        {
            if (FolderExists(favorite.Path))
            {
                rows.Add(Row(favorite.Name.Length == 0 ? favorite.Path : favorite.Name, favorite.Path, 0, 4));
            }
        }

        return rows;
    }

    /// <summary>
    /// A drive as the OS file explorer shows it: "Local disk (C:)" (or its label), how full it is for the bar under the
    /// name, "802.8 GB free of 999.0 GB" in the Date column (<see cref="Entry.FillText"/>) and the total in the Size column.
    /// </summary>
    private Entry DriveRow(DriveEntry drive, int group)
    {
        var shortName = DriveList.ShortName(drive.Path);
        var label = drive.Label.Length == 0 || drive.Label == drive.Path ? (group == 1 ? _text.Get("drives.local_disk") : "") : drive.Label;
        var name = label.Length == 0 ? drive.Path : label + " (" + shortName + ")";
        var row = Row(name, drive.Path, drive.TotalBytes, group);
        row.SizeText = "";
        if (drive.TotalBytes > 0)
        {
            row.UsedFraction = Math.Clamp(1.0 - ((double)drive.FreeBytes / drive.TotalBytes), 0, 1);
            row.SizeText = LocalFileSystem.FormatSize(drive.TotalBytes);
            row.FillText = _text.Get("drives.free_of")
                .Replace("{0}", LocalFileSystem.FormatSize(drive.FreeBytes), StringComparison.Ordinal)
                .Replace("{1}", LocalFileSystem.FormatSize(drive.TotalBytes), StringComparison.Ordinal);
        }

        return row;
    }

    private static Entry Row(string name, string path, ulong size, int group)
    {
        return new Entry(name, path, true, false, false, size, null, false, false, "") { Group = group };
    }

    private static bool FolderExists(string path)
    {
        try
        {
            return path.Length > 0 && Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private StringComparer PathComparer() => _windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // A bare "Z:" counts as "Z:\"; a trailing separator never matters.
    private string Normalize(string path)
    {
        var text = _windows ? path.Replace('/', '\\') : path;
        return text.TrimEnd(_windows ? '\\' : '/');
    }

    private string SharePath(string path) =>
        _windows && path.Length == 2 && path[1] == ':' ? path + "\\" : path;
}

/// <summary>What disk usage scans at Computer: one <see cref="Folder"/>, or (when null) every one of <see cref="Drives"/>.</summary>
public sealed record ComputerScanTarget(string? Folder, IReadOnlyList<string> Drives);
