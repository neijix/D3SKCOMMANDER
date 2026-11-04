using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.FileOps;

internal static partial class FileCopy
{
    private static async Task<OperationError?> WriteFileAsync(
        TransferWork work,
        string source,
        string destination,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        await RemoveReparsePointAsync(work, destination).ConfigureAwait(false);
        var opened = await work.Destination.OpenWriteAsync(destination, CancellationToken.None).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            return opened.Error ?? OperationError.Other("the file could not be created", destination);
        }

        return await PumpToAsync(work, source, destination, opened.Value, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RemoveReparsePointAsync(TransferWork work, string path)
    {
        var entry = await work.Destination.GetEntryAsync(path, CancellationToken.None).ConfigureAwait(false);
        if (!entry.IsSuccess || !entry.Value.IsLink)
        {
            return;
        }

        await work.Destination.DeleteAsync(path, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<OperationError?> PumpToAsync(
        TransferWork work,
        string source,
        string destination,
        Stream output,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        OperationError? error;
        try
        {
            error = await PumpAsync(work, source, output, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await output.DisposeAsync().ConfigureAwait(false);
        }

        if (error is null)
        {
            LocalMetadata.Apply(work.Source, source, work.Destination, destination);
            return null;
        }

        await work.RemovePartialAsync(destination).ConfigureAwait(false);
        return error;
    }

    private static async Task<OperationError?> PumpAsync(
        TransferWork work,
        string source,
        Stream output,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var opened = await work.Source.OpenReadAsync(source, cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
        {
            return opened.Error ?? OperationError.NotFound(source);
        }

        await using var input = opened.Value;
        var buffer = new byte[Chunk(input.CanSeek ? input.Length : BufferSize)];
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return OperationError.Cancelled(source);
            }

            var read = await ReadChunkAsync(input, buffer, source, cancellationToken).ConfigureAwait(false);
            if (read.Error is not null)
            {
                return read.Error;
            }

            if (read.Count == 0)
            {
                return null;
            }

            var written = await WriteChunkAsync(output, buffer, read.Count, source, cancellationToken).ConfigureAwait(false);
            if (written is not null)
            {
                return written;
            }

            work.Session.DoneBytes += read.Count;
            work.Report(progress, false);
        }
    }

    private static async Task<(int Count, OperationError? Error)> ReadChunkAsync(
        Stream input,
        byte[] buffer,
        string source,
        CancellationToken cancellationToken)
    {
        try
        {
            var count = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            return (count, null);
        }
        catch (OperationCanceledException)
        {
            return (0, OperationError.Cancelled(source));
        }
        catch (Exception ex) when (LocalMetadata.IsFileSystem(ex))
        {
            return (0, OperationError.FromException(ex, source));
        }
    }

    private static async Task<OperationError?> WriteChunkAsync(
        Stream output,
        byte[] buffer,
        int count,
        string source,
        CancellationToken cancellationToken)
    {
        try
        {
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            return OperationError.Cancelled(source);
        }
        catch (Exception ex) when (LocalMetadata.IsFileSystem(ex))
        {
            return OperationError.FromException(ex, source);
        }
    }

    private static int Chunk(long size)
    {
        if (size <= 4096)
        {
            return 4096;
        }

        if (size >= BufferSize)
        {
            return BufferSize;
        }

        return (int)size;
    }
}
