using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using FluentFTP;
using FluentFTP.Exceptions;

namespace D3skCommander.Core.Ftp;

public sealed partial class FtpConnection
{
    /// <summary>
    /// The entries of a remote folder, folders first (<see cref="FtpPaths.Sort"/>), without
    /// <c>.</c> and <c>..</c>. MLSD when the server has it, otherwise LIST, both through
    /// <see cref="FtpListing"/>. A missing folder is <see cref="ErrorKind.NotFound"/>.
    /// </summary>
    public async Task<FileResult<IReadOnlyList<RemoteEntry>>> ListAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileResult<IReadOnlyList<RemoteEntry>>.Fail(WithPath(turn.Error, directory));
        }

        return await ListLockedAsync(directory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One remote path. The root folder is a synthetic folder entry.</summary>
    public async Task<FileResult<RemoteEntry>> GetEntryAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileResult<RemoteEntry>.Fail(WithPath(turn.Error, path));
        }

        return await EntryLockedAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FileResult<RemoteEntry>> EntryLockedAsync(string path, CancellationToken cancellationToken)
    {
        var name = FtpPaths.Name(path);
        if (name == "/" || name == "." || name == "..")
        {
            return FileResult<RemoteEntry>.Ok(new RemoteEntry(name, true, 0, null, ""));
        }

        var parent = FtpPaths.Parent(path);
        var listed = await ListLockedAsync(parent.Length == 0 ? "." : parent, cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            return FileResult<RemoteEntry>.Fail(listed.Error! with { Path = path });
        }

        foreach (var entry in listed.Value)
        {
            if (entry.Name == name)
            {
                return FileResult<RemoteEntry>.Ok(entry);
            }
        }

        return FileResult<RemoteEntry>.Fail(OperationError.NotFound(path));
    }

    private async Task<FileResult<IReadOnlyList<RemoteEntry>>> ListLockedAsync(string directory, CancellationToken cancellationToken)
    {
        try
        {
            var read = await ReadListingAsync("MLSD", directory, cancellationToken).ConfigureAwait(false);
            if (read is null)
            {
                read = await ReadListingAsync("LIST", directory, cancellationToken, required: true).ConfigureAwait(false);
            }

            var (mlsd, lines) = read!.Value;
            var entries = mlsd ? FtpListing.EntriesFrom(lines, null) : FtpListing.EntriesFrom(null, lines);
            if (entries.Count == 0 && !await FolderExistsAsync(directory, cancellationToken).ConfigureAwait(false))
            {
                return FileResult<IReadOnlyList<RemoteEntry>>.Fail(OperationError.NotFound(directory));
            }

            var sorted = new List<RemoteEntry>(entries);
            FtpPaths.Sort(sorted);
            return FileResult<IReadOnlyList<RemoteEntry>>.Ok(sorted);
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            return FileResult<IReadOnlyList<RemoteEntry>>.Fail(FtpErrors.Describe(ex, directory));
        }
    }

    /// <summary>The lines of one listing command. A refused MLSD is null (so LIST runs); a refused LIST throws.</summary>
    private async Task<(bool Mlsd, List<string> Lines)?> ReadListingAsync(
        string verb,
        string directory,
        CancellationToken cancellationToken,
        bool required = false)
    {
        var command = directory == "." ? verb : verb + " " + directory;
        FtpDataStream stream;
        try
        {
            stream = await _client.OpenListing(command, cancellationToken).ConfigureAwait(false);
        }
        catch (FtpCommandException) when (!required)
        {
            return null;
        }

        var lines = new List<string>();
        try
        {
            using var reader = new StreamReader(stream, _client.Encoding);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                lines.Add(line);
            }
        }
        finally
        {
            await CloseDataAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return (verb == "MLSD", lines);
    }

    /// <summary>A server may answer an empty listing for a folder that is not there, so an empty one is checked.</summary>
    private async Task<bool> FolderExistsAsync(string directory, CancellationToken cancellationToken) =>
        directory == "/" || directory == "." || await _client.DirectoryExists(directory, cancellationToken).ConfigureAwait(false);

    private static async Task CloseDataAsync(FtpDataStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await stream.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FtpException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static OperationError WithPath(OperationError error, string path) =>
        error.Path is null ? error with { Path = path } : error;
}
