using D3skCommander.Core.Jobs;

namespace D3skCommander.Core.Checksums;

/// <summary>Create and verify checksum lists as jobs. Cancel leaves a create's output unwritten.</summary>
public sealed class ChecksumService
{
    /// <summary>English title of a create job, the command label without waiting on the localizer.</summary>
    public const string CreateTitle = "Create checksum file\u2026";

    public const string VerifyTitle = "Verify checksums";

    private readonly IJobService _jobs;

    public ChecksumService(IJobService jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        _jobs = jobs;
    }

    /// <summary>
    /// One list at <paramref name="outputPath"/> (ends trimmed), or one list beside each file
    /// when <paramref name="oncePerFile"/> is set. The per-file name is <see cref="Checksum.OutputPath"/>.
    /// </summary>
    public ChecksumOperation Create(
        IReadOnlyList<string> files,
        string baseDirectory,
        ChecksumAlgo algo,
        bool oncePerFile,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        ArgumentNullException.ThrowIfNull(outputPath);
        var sources = Copy(files);
        var operation = new ChecksumOperation();
        var job = _jobs.Start(CreateTitle, (progress, cancellationToken) =>
        {
            RunCreate(operation, sources, baseDirectory, algo, oncePerFile, outputPath, progress, cancellationToken);
            return Task.CompletedTask;
        });
        operation.Attach(job);
        return operation;
    }

    /// <summary>Verify each list. A mismatch or a missing file is a verdict; the job still completes.</summary>
    public ChecksumOperation Verify(IReadOnlyList<string> lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var sources = Copy(lists);
        var operation = new ChecksumOperation();
        var job = _jobs.Start(VerifyTitle, (progress, cancellationToken) =>
        {
            RunVerify(operation, sources, progress, cancellationToken);
            return Task.CompletedTask;
        });
        operation.Attach(job);
        return operation;
    }

    private static void RunCreate(
        ChecksumOperation operation,
        IReadOnlyList<string> files,
        string baseDirectory,
        ChecksumAlgo algo,
        bool oncePerFile,
        string outputPath,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        if (oncePerFile)
        {
            WriteEach(operation, files, baseDirectory, algo, progress, cancellationToken);
            return;
        }

        WriteCombined(operation, files, baseDirectory, algo, outputPath.Trim(), progress, cancellationToken);
    }

    private static void WriteEach(
        ChecksumOperation operation,
        IReadOnlyList<string> files,
        string baseDirectory,
        ChecksumAlgo algo,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = TotalBytes(files);
        long hashed = 0;
        var done = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(directory))
            {
                directory = baseDirectory;
            }

            var target = Checksum.OutputPath([file], directory, algo, false);
            Report(progress, done, files.Count, hashed, totalBytes, file);
            var written = Checksum.WriteChecksumFile(
                [file],
                directory,
                algo,
                target,
                cancellationToken,
                n =>
                {
                    hashed += n;
                    Report(progress, done, files.Count, hashed, totalBytes, file);
                });
            done++;
            operation.Wrote(written);
        }

        Report(progress, done, files.Count, hashed, Math.Max(totalBytes, hashed), files.Count == 0 ? "" : files[^1]);
    }

    private static void WriteCombined(
        ChecksumOperation operation,
        IReadOnlyList<string> files,
        string baseDirectory,
        ChecksumAlgo algo,
        string outputPath,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = TotalBytes(files);
        long hashed = 0;
        Report(progress, 0, files.Count, 0, totalBytes, outputPath);
        var written = Checksum.WriteChecksumFile(
            files,
            baseDirectory,
            algo,
            outputPath,
            cancellationToken,
            n =>
            {
                hashed += n;
                Report(progress, 0, files.Count, hashed, totalBytes, outputPath);
            });
        operation.Wrote(written);
        Report(progress, files.Count, files.Count, hashed, Math.Max(totalBytes, hashed), outputPath);
    }

    private static void RunVerify(
        ChecksumOperation operation,
        IReadOnlyList<string> lists,
        IJobProgress progress,
        CancellationToken cancellationToken)
    {
        var done = 0;
        foreach (var list in lists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, done, lists.Count, 0, 0, list);
            foreach (var verdict in Checksum.VerifyChecksumFile(list, cancellationToken))
            {
                operation.Add(verdict);
            }

            done++;
        }

        Report(progress, done, lists.Count, 0, 0, lists.Count == 0 ? "" : lists[^1]);
    }

    private static void Report(IJobProgress progress, int done, int total, long bytesDone, long bytesTotal, string name)
    {
        progress.Report(new JobProgress(done, total, bytesDone, bytesTotal, name));
    }

    private static long TotalBytes(IReadOnlyList<string> files)
    {
        long total = 0;
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Exists)
                {
                    total += info.Length;
                }
            }
            catch (IOException)
            {
                // The hash reports the real failure.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as a missing length: the hash still runs.
            }
        }

        return total;
    }

    private static string[] Copy(IReadOnlyList<string> paths)
    {
        var copy = new string[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            ArgumentException.ThrowIfNullOrEmpty(path);
            copy[i] = path;
        }

        return copy;
    }
}
