using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Providers;
using Xunit;
using OpLog = D3skCommander.Core.OperationLog.OperationLog;

namespace D3skCommander.Core.Tests.FileOps;

/// <summary>
/// P10: copy, move, delete and new folder run as jobs with progress and cancel.
/// Ports <c>src/core/fileops/tests.rs</c> except the trash test.
/// </summary>
public partial class FileOpsTests
{
    internal const long OldTime = 1_500_000_000;
    internal const long NewTime = 1_700_000_000;
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    internal sealed class Bench : IDisposable
    {
        public Bench()
        {
            Root = Path.Combine(Path.GetTempPath(), "d3sk-ops-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Provider = new LocalFileSystemProvider();
            Jobs = new JobService();
            Log = new OpLog(error: new StringWriter());
            Ops = new FileOperationService(Jobs, Log);
        }

        public string Root { get; }

        public LocalFileSystemProvider Provider { get; }

        public JobService Jobs { get; }

        public OpLog Log { get; }

        public FileOperationService Ops { get; }

        public string PathOf(params string[] parts)
        {
            var path = Root;
            foreach (var part in parts)
            {
                path = Path.Combine(path, part);
            }

            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    internal readonly struct Outcome
    {
        public Outcome(FileOperation operation, JobResult result)
        {
            Operation = operation;
            Result = result;
        }

        public FileOperation Operation { get; }

        public JobResult Result { get; }

        public (int Done, int Errors, bool Cancelled) Finished =>
            (Operation.CompletedFiles, Operation.ErrorCount, Result.Outcome == JobOutcome.Cancelled);
    }

    internal static async Task<Outcome> Go(
        Bench bench,
        FileOpKind kind,
        string[] sources,
        string? destination,
        string? renameTo = null,
        ConflictChoice? choice = null,
        CancellationTokenSource? cancel = null,
        Action<FileOpProgress>? onProgress = null,
        Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>>? answer = null)
    {
        var token = cancel is null ? CancellationToken.None : cancel.Token;
        var operation = bench.Ops.Start(
            Request(bench, kind, sources, destination, renameTo),
            answer ?? Answer(choice),
            onProgress,
            token);
        var result = await operation.Job.Completion.WaitAsync(Bound);
        return new Outcome(operation, result);
    }

    private static FileOpRequest Request(Bench bench, FileOpKind kind, string[] sources, string? destination, string? renameTo)
    {
        IFileSystemProvider? target = destination is null ? null : bench.Provider;
        return new FileOpRequest(kind, bench.Provider, sources, target, destination, renameTo);
    }

    private static Func<ConflictQuestion, CancellationToken, Task<ConflictChoice>> Answer(ConflictChoice? choice)
    {
        return (_, _) =>
        {
            if (choice is null)
            {
                throw new InvalidOperationException("unexpected conflict");
            }

            return Task.FromResult(choice.Value);
        };
    }

    internal static void WriteText(string path, string text)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, text);
    }

    internal static void SetMtime(string path, long unixSeconds)
    {
        File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(unixSeconds));
    }

    private static long Mtime(string path)
    {
        var utc = File.GetLastWriteTimeUtc(path);
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
    }
}
