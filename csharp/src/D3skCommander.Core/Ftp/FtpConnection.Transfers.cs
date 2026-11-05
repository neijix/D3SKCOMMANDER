using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using FluentFTP;

namespace D3skCommander.Core.Ftp;

/// <summary>Bytes moved by a finished transfer.</summary>
public sealed record FtpTransfer(long Bytes);

public sealed partial class FtpConnection
{
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// Downloads <paramref name="remote"/> into <paramref name="local"/> (replaced). The bytes
    /// arrive in a temporary file beside it that is moved into place when complete, so a failed or
    /// cancelled download leaves no partial file and keeps an older one. <paramref name="progress"/>
    /// receives the bytes so far.
    /// </summary>
    public async Task<FileResult<FtpTransfer>> DownloadAsync(
        string remote,
        string local,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(remote);
        ArgumentException.ThrowIfNullOrEmpty(local);
        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileResult<FtpTransfer>.Fail(WithPath(turn.Error, remote));
        }

        var partial = local + ".d3part-" + Guid.NewGuid().ToString("N");
        try
        {
            var bytes = await DownloadToAsync(remote, partial, progress, cancellationToken).ConfigureAwait(false);
            File.Move(partial, local, overwrite: true);
            return FileResult<FtpTransfer>.Ok(new FtpTransfer(bytes));
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            DeleteQuietly(partial);
            return FileResult<FtpTransfer>.Fail(FtpErrors.Describe(ex, remote));
        }
    }

    /// <summary>
    /// Uploads <paramref name="local"/> as <paramref name="remote"/>. A failed or cancelled upload
    /// removes the partial remote file (best effort).
    /// </summary>
    public async Task<FileResult<FtpTransfer>> UploadAsync(
        string local,
        string remote,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(local);
        ArgumentException.ThrowIfNullOrEmpty(remote);
        using var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        if (turn.Error is not null)
        {
            return FileResult<FtpTransfer>.Fail(WithPath(turn.Error, remote));
        }

        try
        {
            var bytes = await UploadFromAsync(local, remote, progress, cancellationToken).ConfigureAwait(false);
            return FileResult<FtpTransfer>.Ok(new FtpTransfer(bytes));
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            await RemoveQuietlyAsync(remote).ConfigureAwait(false);
            return FileResult<FtpTransfer>.Fail(FtpErrors.Describe(ex, remote));
        }
    }

    /// <summary>
    /// Opens a remote file for reading. The caller owns the stream; the connection is busy until
    /// it is disposed. A missing file is not found; a folder is invalid data.
    /// </summary>
    public async Task<FileResult<Stream>> OpenReadAsync(string remote, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(remote);
        var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        using (turn)
        {
            if (turn.Error is not null)
            {
                return FileResult<Stream>.Fail(WithPath(turn.Error, remote));
            }

            var entry = await EntryLockedAsync(remote, cancellationToken).ConfigureAwait(false);
            if (!entry.IsSuccess)
            {
                return FileResult<Stream>.Fail(entry.Error!);
            }

            if (entry.Value.IsDirectory)
            {
                return FileResult<Stream>.Fail(OperationError.InvalidData("is a folder", remote));
            }

            return await OpenStreamAsync(turn, remote, write: false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates or truncates a remote file and opens it for writing. Disposing the stream ends the
    /// upload. A folder is invalid data.
    /// </summary>
    public async Task<FileResult<Stream>> OpenWriteAsync(string remote, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(remote);
        var turn = await TakeTurnAsync(cancellationToken).ConfigureAwait(false);
        using (turn)
        {
            if (turn.Error is not null)
            {
                return FileResult<Stream>.Fail(WithPath(turn.Error, remote));
            }

            var entry = await EntryLockedAsync(remote, cancellationToken).ConfigureAwait(false);
            if (entry.IsSuccess && entry.Value.IsDirectory)
            {
                return FileResult<Stream>.Fail(OperationError.InvalidData("is a folder", remote));
            }

            return await OpenStreamAsync(turn, remote, write: true, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<FileResult<Stream>> OpenStreamAsync(Turn turn, string remote, bool write, CancellationToken cancellationToken)
    {
        try
        {
            Stream stream = write
                ? await _client.OpenWrite(remote, FtpDataType.Binary, false, cancellationToken).ConfigureAwait(false)
                : await _client.OpenRead(remote, FtpDataType.Binary, 0, false, cancellationToken).ConfigureAwait(false);
            return FileResult<Stream>.Ok(new GatedStream(stream, turn.Keep()));
        }
        catch (Exception ex) when (IsFailure(ex))
        {
            return FileResult<Stream>.Fail(FtpErrors.Describe(ex, remote));
        }
    }

    private async Task<long> DownloadToAsync(string remote, string partial, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var source = await _client.OpenRead(remote, FtpDataType.Binary, 0, false, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, ChunkSize, true);
            return await CopyAsync(source, target, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await AbandonAsync(source).ConfigureAwait(false);
        }
    }

    private async Task<long> UploadFromAsync(string local, string remote, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
        var target = await _client.OpenWrite(remote, FtpDataType.Binary, false, cancellationToken).ConfigureAwait(false);
        long total;
        try
        {
            total = await CopyAsync(source, target, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await AbandonAsync(target).ConfigureAwait(false);
            throw;
        }

        await DataStreams.CloseAsync(target, cancellationToken).ConfigureAwait(false);
        return total;
    }

    private static async Task<long> CopyAsync(Stream source, Stream target, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkSize];
        long total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return total;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            progress?.Report(total);
        }
    }

    private static async Task AbandonAsync(Stream stream)
    {
        try
        {
            await DataStreams.CloseAsync(stream).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FluentFTP.Exceptions.FtpException or IOException)
        {
        }
    }

    private async Task RemoveQuietlyAsync(string remote)
    {
        try
        {
            await _client.DeleteFile(remote, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsFailure(ex))
        {
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
