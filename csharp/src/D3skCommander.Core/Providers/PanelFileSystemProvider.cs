using System.Runtime.CompilerServices;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Panels;

namespace D3skCommander.Core.Providers;

/// <summary>
/// The provider every panel browses through: the Computer address, and any path under it, goes to the
/// read-only Computer provider; every other path goes to the local provider unchanged, except that a listing
/// whose path goes up to Computer (<see cref="ComputerAddress.GoesUp"/>) has exactly one ".." row, to the address.
/// </summary>
public sealed class PanelFileSystemProvider : IFileSystemProvider
{
    /// <summary>Where removable volumes are mounted: a mount strictly under one of these goes up to Computer.</summary>
    public static readonly IReadOnlyList<string> DefaultPrefixes = ["/media", "/run/media", "/mnt", "/Volumes"];

    private readonly IFileSystemProvider _local;
    private readonly IFileSystemProvider _computer;
    private readonly IComputerSources _sources;
    private readonly string[] _prefixes;
    private readonly bool _windows;

    /// <param name="prefixes">Removable-mount prefixes; default <see cref="DefaultPrefixes"/>.</param>
    /// <param name="windows">Case-insensitive paths and drive roots; default: the current OS.</param>
    public PanelFileSystemProvider(
        IFileSystemProvider local,
        IFileSystemProvider computer,
        IComputerSources sources,
        IEnumerable<string>? prefixes = null,
        bool? windows = null)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(computer);
        ArgumentNullException.ThrowIfNull(sources);
        _local = local;
        _computer = computer;
        _sources = sources;
        _prefixes = [.. prefixes ?? DefaultPrefixes];
        _windows = windows ?? OperatingSystem.IsWindows();
    }

    public bool IsReadOnly => _local.IsReadOnly;

    public async IAsyncEnumerable<FileResult<Entry>> ListAsync(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (ComputerAddress.IsAddressOrUnder(directory))
        {
            await foreach (var item in _computer.ListAsync(directory, showHidden, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        var listing = _local.ListAsync(directory, showHidden, cancellationToken);
        if (!GoesUpToComputer(directory))
        {
            await foreach (var item in listing.ConfigureAwait(false))
            {
                yield return item;
            }

            yield break;
        }

        // The ".." row to Computer comes first and stands in for the local one; a failed listing gets none,
        // and a listing with no rows still gets the one.
        var first = true;
        var failed = false;
        await foreach (var item in listing.ConfigureAwait(false))
        {
            if (first && item.IsSuccess)
            {
                yield return ParentRow();
            }

            first = false;
            failed |= !item.IsSuccess;
            if (!item.IsSuccess || !item.Value.IsParent)
            {
                yield return item;
            }
        }

        if (first && !failed)
        {
            yield return ParentRow();
        }
    }

    private static FileResult<Entry> ParentRow() =>
        FileResult<Entry>.Ok(new Entry("..", ComputerAddress.Text, true, false, true, 0, null, false, false, ""));

    public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) =>
        Route(path).GetEntryAsync(path, cancellationToken);

    public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        Route(path).OpenReadAsync(path, cancellationToken);

    public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) =>
        Route(path).OpenWriteAsync(path, cancellationToken);

    public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
        Route(path).CreateFolderAsync(path, cancellationToken);

    public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) =>
        Route(path).RenameAsync(path, newName, cancellationToken);

    public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) =>
        Route(path).DeleteAsync(path, cancellationToken);

    private IFileSystemProvider Route(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return ComputerAddress.IsAddressOrUnder(path) ? _computer : _local;
    }

    // A path root always goes up; any other path only when it is a mount under a prefix. The OS mount points
    // are read just for the second case, so an ordinary folder reads no OS state.
    private bool GoesUpToComputer(string path)
    {
        if (ComputerAddress.GoesUp(path, [], [], _windows))
        {
            return true;
        }

        if (!ComputerAddress.GoesUp(path, [path], _prefixes, _windows))
        {
            return false;
        }

        try
        {
            return ComputerAddress.GoesUp(path, _sources.MountPoints(), _prefixes, _windows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
}
