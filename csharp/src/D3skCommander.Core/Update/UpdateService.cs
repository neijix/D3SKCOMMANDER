using System.Security.Cryptography;
using D3skCommander.Core.Common;
using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Update;

/// <summary>Downloads one release file on the job service and checks its SHA-256.</summary>
public sealed class UpdateService : IDisposable
{
    private readonly IJobService _jobs;
    private readonly IAssetSource _source;
    private readonly IDisposable? _owned;

    public UpdateService(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var http = new HttpAssetSource(UpdateMetadata.CurrentVersion);
        _jobs = jobs;
        _source = http;
        _owned = http;
    }

    public UpdateService(IJobService jobs, IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(source);
        _jobs = jobs;
        _source = source;
    }

    /// <summary>
    /// Queues the download of <paramref name="asset"/> into <paramref name="directory"/>.
    /// <paramref name="sums"/> is the <c>SHA256SUMS</c> text, used when the asset has no digest.
    /// A missing checksum or a mismatch removes nothing that was already installed; the partial file is removed.
    /// </summary>
    public UpdateDownload Download(
        ReleaseAsset asset,
        string directory,
        string? sums,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var session = new DownloadSession();
        var ready = new TaskCompletionSource<IJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = _jobs.Start("Download", [new JobItem(asset.Name)], async (_, progress, token) =>
        {
            var self = await ready.Task.WaitAsync(token).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            try
            {
                if (linked.IsCancellationRequested)
                {
                    self.Cancel();
                    throw new OperationCanceledException(linked.Token);
                }

                return await SaveAsync(asset, directory, sums, progress, linked.Token, session).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                self.Cancel();
                throw;
            }
        });
        ready.SetResult(job);
        return new UpdateDownload(job, session);
    }

    public void Dispose() => _owned?.Dispose();

    private async Task<ItemResult> SaveAsync(
        ReleaseAsset asset,
        string directory,
        string? sums,
        IJobProgress progress,
        CancellationToken cancellationToken,
        DownloadSession session)
    {
        var expected = ReleaseChecksum.Expected(asset, sums);
        if (expected is null)
        {
            return ItemResult.Failed(OperationError.InvalidData(
                asset.Name + ": no checksum published, not installed",
                asset.Name));
        }

        Directory.CreateDirectory(directory);
        var dest = Path.Combine(directory, asset.Name);
        try
        {
            var got = await CopyAsync(asset, dest, progress, cancellationToken).ConfigureAwait(false);
            return Finish(asset, dest, expected, got, session);
        }
        catch (OperationCanceledException)
        {
            Delete(dest);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return ItemResult.Failed(OperationError.Network(ex.Message, asset.Name));
        }
    }

    private async Task<string> CopyAsync(
        ReleaseAsset asset,
        string dest,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        await using var input = await _source.OpenAsync(asset.Url, cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            dest,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long done = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            hasher.AppendData(buffer.AsSpan(0, count));
            done += count;
            progress.Report(new JobProgress(0, 1, done, asset.Size, asset.Name));
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        return ReleaseChecksum.Format(hasher.GetHashAndReset());
    }

    private static ItemResult Finish(
        ReleaseAsset asset,
        string dest,
        string expected,
        string got,
        DownloadSession session)
    {
        if (!string.Equals(got, expected, StringComparison.Ordinal))
        {
            Delete(dest);
            return ItemResult.Failed(OperationError.InvalidData(
                asset.Name + ": checksum mismatch (expected " + expected + ", got " + got + ")",
                asset.Name));
        }

        session.Path = dest;
        return ItemResult.Success;
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked partial file is left for the caller to see.
        }
    }
}
