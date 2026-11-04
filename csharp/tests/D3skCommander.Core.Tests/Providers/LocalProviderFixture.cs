using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.Core.Tests.Providers;

public sealed class LocalProviderFixture : ProviderFixture
{
    private readonly string _root;

    public LocalProviderFixture(bool readOnly = false)
    {
        _root = System.IO.Directory.CreateTempSubdirectory("d3sk-provider-").FullName;
        Directory = Path.Combine(_root, "folder");
        System.IO.Directory.CreateDirectory(Directory);
        SampleBytes = [1, 2, 3, 4, 5];
        FileNames = ["a.txt", "b.txt", "c.txt"];
        foreach (var name in FileNames)
        {
            File.WriteAllBytes(Path.Combine(Directory, name), SampleBytes);
        }

        File.WriteAllBytes(Path.Combine(Directory, ".hidden"), SampleBytes);
        SampleFileName = FileNames[0];
        SampleFilePath = Combine(Directory, SampleFileName);
        var local = new LocalFileSystemProvider();
        Provider = readOnly ? new ReadOnlyView(local) : local;
    }

    public override IFileSystemProvider Provider { get; }

    public override string Directory { get; }

    public override IReadOnlyList<string> FileNames { get; }

    public override string SampleFileName { get; }

    public override string SampleFilePath { get; }

    public override byte[] SampleBytes { get; }

    public override string Combine(string directory, string name) => Path.Combine(directory, name);

    public override void Dispose() => System.IO.Directory.Delete(_root, recursive: true);

    /// <summary>
    /// Local behind the read-only flag. Mutation cores throw: the base must refuse first.
    /// </summary>
    private sealed class ReadOnlyView : FileSystemProvider
    {
        private readonly LocalFileSystemProvider _inner;

        public ReadOnlyView(LocalFileSystemProvider inner) => _inner = inner;

        public override bool IsReadOnly => true;

        protected override IAsyncEnumerable<FileResult<Entry>> ListCore(
            string directory,
            bool showHidden,
            CancellationToken cancellationToken) =>
            _inner.ListAsync(directory, showHidden, cancellationToken);

        protected override Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken) =>
            _inner.GetEntryAsync(path, cancellationToken);

        protected override Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken) =>
            _inner.OpenReadAsync(path, cancellationToken);

        protected override Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken) =>
            Refused<Stream>();

        protected override Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken) =>
            RefusedStatus();

        protected override Task<FileResult<string>> RenameCore(
            string path,
            string newName,
            CancellationToken cancellationToken) =>
            Refused<string>();

        protected override Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken) =>
            RefusedStatus();

        private static Task<FileResult<T>> Refused<T>()
            where T : class =>
            throw new InvalidOperationException("a read-only provider ran a mutation");

        private static Task<FileStatus> RefusedStatus() =>
            throw new InvalidOperationException("a read-only provider ran a mutation");
    }
}
