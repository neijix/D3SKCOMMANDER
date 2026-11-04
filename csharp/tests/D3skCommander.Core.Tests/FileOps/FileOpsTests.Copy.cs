using D3skCommander.Core.Common;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public partial class FileOpsTests
{
    [Fact]
    public async Task CopyFileKeepsContentAndMtime()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "hello");
        SetMtime(src, OldTime);
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal(OldTime, Mtime(Path.Combine(dest, "a.txt")));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public async Task CopyFileKeepsPermissions()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "unix file modes are not asserted on Windows");
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var bench = new Bench();
        var src = bench.PathOf("run.sh");
        WriteText(src, "#!/bin/sh\n");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(src, mode);
        var dest = bench.PathOf("out");

        await Go(bench, FileOpKind.Copy, [src], dest);

        var copied = (int)File.GetUnixFileMode(Path.Combine(dest, "run.sh")) & 0x1FF;
        Assert.Equal((int)mode, copied);
    }

    [Fact]
    public async Task CopyTreeWithEmptyDirsAndTotals()
    {
        using var bench = new Bench();
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "top.txt"), "12345");
        WriteText(Path.Combine(tree, "sub", "deep", "leaf.txt"), "abc");
        Directory.CreateDirectory(Path.Combine(tree, "empty"));
        SetMtime(Path.Combine(tree, "sub", "deep", "leaf.txt"), OldTime);
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [tree], dest);

        Assert.Equal((2, 0, false), outcome.Finished);
        Assert.Equal("12345", File.ReadAllText(Path.Combine(dest, "tree", "top.txt")));
        Assert.Equal("abc", File.ReadAllText(Path.Combine(dest, "tree", "sub", "deep", "leaf.txt")));
        Assert.True(Directory.Exists(Path.Combine(dest, "tree", "empty")));
        Assert.Equal(OldTime, Mtime(Path.Combine(dest, "tree", "sub", "deep", "leaf.txt")));
        var progress = outcome.Operation.Job.Progress;
        Assert.Equal((2, 2), (progress.ItemsTotal, progress.ItemsDone));
        Assert.Equal((8L, 8L), (progress.BytesTotal, progress.BytesDone));
    }

    [Fact]
    public async Task CopyALargeFileInSeveralChunks()
    {
        using var bench = new Bench();
        var src = bench.PathOf("big.bin");
        var data = new byte[(2 * 1024 * 1024) + 123];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        File.WriteAllBytes(src, data);
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest);

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dest, "big.bin")));
        Assert.Equal(data.Length, outcome.Operation.Job.Progress.BytesDone);
    }

    [Fact]
    public async Task CopyWithRenameTo()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest, "b.txt");

        Assert.Equal((1, 0, false), outcome.Finished);
        Assert.Equal("x", File.ReadAllText(Path.Combine(dest, "b.txt")));
        Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
    }

    [Fact]
    public async Task RenameToWithASeparatorIsRefused()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [src], dest, "../evil.txt");

        Assert.Equal((0, 1, false), outcome.Finished);
        Assert.False(File.Exists(bench.PathOf("evil.txt")));
    }

    [Fact]
    public async Task CopyWithoutADestinationIsAnError()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "x");

        var outcome = await Go(bench, FileOpKind.Copy, [src], null);

        Assert.Equal((0, 1, false), outcome.Finished);
        Assert.Contains("destination", outcome.Operation.ErrorMessages()[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSourceReportsAnErrorAndGoesOn()
    {
        using var bench = new Bench();
        var good = bench.PathOf("good.txt");
        WriteText(good, "ok");
        var dest = bench.PathOf("out");

        var outcome = await Go(bench, FileOpKind.Copy, [bench.PathOf("nope.txt"), good], dest);

        Assert.Equal((1, 1, false), outcome.Finished);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(dest, "good.txt")));
    }

    [Fact]
    public async Task CopyAFolderIntoItselfIsRefused()
    {
        using var bench = new Bench();
        var tree = bench.PathOf("tree");
        WriteText(Path.Combine(tree, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));

        foreach (var dest in new[] { tree, Path.Combine(tree, "sub") })
        {
            var outcome = await Go(bench, FileOpKind.Copy, [tree], dest);
            Assert.Equal((0, 1, false), outcome.Finished);
            Assert.Contains("into itself", outcome.Operation.ErrorMessages()[0], StringComparison.Ordinal);
        }

        Assert.False(Directory.Exists(Path.Combine(tree, "tree")));
        Assert.False(Directory.Exists(Path.Combine(tree, "sub", "tree")));
    }

    [Fact]
    public async Task CopyOntoTheSamePathIsAnError()
    {
        using var bench = new Bench();
        var src = bench.PathOf("a.txt");
        WriteText(src, "keep me");

        var outcome = await Go(bench, FileOpKind.Copy, [src], bench.Root);

        Assert.Equal((0, 1, false), outcome.Finished);
        Assert.Contains("same", outcome.Operation.ErrorMessages()[0], StringComparison.Ordinal);
        Assert.Equal("keep me", File.ReadAllText(src));
    }

    [Fact]
    public async Task FailedOrCancelledOverwriteKeepsTheOldFileAndLeavesNoTempFile()
    {
        using var bench = new Bench();
        var src = bench.PathOf("src", "a.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        var sourceBytes = new byte[3 * 1024 * 1024];
        Array.Fill(sourceBytes, (byte)'N');
        File.WriteAllBytes(src, sourceBytes);
        var dest = bench.PathOf("out");
        var target = Path.Combine(dest, "a.bin");
        WriteText(target, "keep original bytes");

        var halfFailing = new HalfFailingProvider(bench.Provider, sourceBytes.Length / 2);
        var failedOp = bench.Ops.Start(
            new FileOpRequest(FileOpKind.Copy, halfFailing, [src], bench.Provider, dest),
            Answer(ConflictChoice.Overwrite),
            null,
            TestContext.Current.CancellationToken);
        var failedResult = await failedOp.Job.Completion.WaitAsync(Bound, TestContext.Current.CancellationToken);
        var failedOutcome = new Outcome(failedOp, failedResult);

        Assert.Equal((0, 1, false), failedOutcome.Finished);
        Assert.Equal("keep original bytes", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFiles(dest));

        using var cancel = new CancellationTokenSource();
        var cancelledOutcome = await Go(
            bench,
            FileOpKind.Copy,
            [src],
            dest,
            choice: ConflictChoice.Overwrite,
            cancel: cancel,
            onProgress: progress =>
            {
                if (progress.CurrentPath == src)
                {
                    cancel.Cancel();
                }
            });

        Assert.Equal((0, 0, true), cancelledOutcome.Finished);
        Assert.Equal("keep original bytes", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFiles(dest));
    }

    private sealed class HalfFailingProvider : IFileSystemProvider
    {
        private readonly IFileSystemProvider _inner;
        private readonly long _failAfterBytes;

        public HalfFailingProvider(IFileSystemProvider inner, long failAfterBytes)
        {
            _inner = inner;
            _failAfterBytes = failAfterBytes;
        }

        public bool IsReadOnly => _inner.IsReadOnly;

        public IAsyncEnumerable<FileResult<Entry>> ListAsync(
            string directory,
            bool showHidden,
            CancellationToken cancellationToken) =>
            _inner.ListAsync(directory, showHidden, cancellationToken);

        public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) =>
            _inner.GetEntryAsync(path, cancellationToken);

        public async Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            var opened = await _inner.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
            if (!opened.IsSuccess)
            {
                return opened;
            }

            return FileResult<Stream>.Ok(new HalfFailingStream(opened.Value, _failAfterBytes));
        }

        public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) =>
            _inner.OpenWriteAsync(path, cancellationToken);

        public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
            _inner.CreateFolderAsync(path, cancellationToken);

        public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) =>
            _inner.RenameAsync(path, newName, cancellationToken);

        public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(path, cancellationToken);
    }

    private sealed class HalfFailingStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _failAfterBytes;
        private long _readSoFar;

        public HalfFailingStream(Stream inner, long failAfterBytes)
        {
            _inner = inner;
            _failAfterBytes = failAfterBytes;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_readSoFar >= _failAfterBytes)
            {
                throw new IOException("simulated read failure at 50%");
            }

            var count = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _readSoFar += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
