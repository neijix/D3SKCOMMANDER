using D3skCommander.Core.Archives;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

public sealed partial class ArchiveFileSystemProvider
{
    protected override Task<FileResult<Stream>> OpenReadCore(string path, CancellationToken cancellationToken)
    {
        var place = Locate(path);
        if (place.Error is not null || place.Archive is null)
        {
            return StreamFail(place.Error ?? OperationError.NotFound(path));
        }

        if (place.Inner.Length == 0)
        {
            return StreamFail(OperationError.InvalidData("not a file", path));
        }

        var listed = ArchiveIo.List(place.Archive);
        if (!listed.IsSuccess)
        {
            return StreamFail(listed.Error ?? OperationError.NotFound(path));
        }

        var member = Find(listed.Value, place.Inner);
        if (member is null)
        {
            return StreamFail(OperationError.NotFound(path));
        }

        if (member.IsDirectory)
        {
            return StreamFail(OperationError.InvalidData("not a file", path));
        }

        return Task.FromResult(ReadMember(place.Archive, place.Inner, path, cancellationToken));
    }

    protected override Task<FileResult<Stream>> OpenWriteCore(string path, CancellationToken cancellationToken) =>
        Refused<Stream>();

    protected override Task<FileStatus> CreateFolderCore(string path, CancellationToken cancellationToken) =>
        RefusedStatus();

    protected override Task<FileResult<string>> RenameCore(string path, string newName, CancellationToken cancellationToken) =>
        Refused<string>();

    protected override Task<FileStatus> DeleteCore(string path, CancellationToken cancellationToken) =>
        RefusedStatus();

    // The base refuses a read-only mutation before this runs. Reaching it is a bug.
    private static Task<FileResult<T>> Refused<T>()
        where T : class =>
        throw new InvalidOperationException("a read-only provider ran a mutation");

    private static Task<FileStatus> RefusedStatus() =>
        throw new InvalidOperationException("a read-only provider ran a mutation");

    private static Task<FileResult<Stream>> StreamFail(OperationError error) =>
        Task.FromResult(FileResult<Stream>.Fail(error));

    private static FileResult<Stream> ReadMember(
        string archive,
        string inner,
        string path,
        CancellationToken cancellationToken)
    {
        var temp = Directory.CreateTempSubdirectory("d3sk-member-").FullName;
        try
        {
            var extracted = ArchiveIo.Extract(archive, new[] { inner }, temp, null, cancellationToken);
            if (!extracted.IsSuccess)
            {
                return FileResult<Stream>.Fail(extracted.Error ?? OperationError.NotFound(path));
            }

            var written = FirstFile(temp);
            if (written is null)
            {
                return FileResult<Stream>.Fail(OperationError.NotFound(path));
            }

            return FileResult<Stream>.Ok(new MemoryStream(File.ReadAllBytes(written)));
        }
        catch (OperationCanceledException)
        {
            return FileResult<Stream>.Fail(OperationError.Cancelled(path));
        }
        finally
        {
            DeleteTemp(temp);
        }
    }

    private static string? FirstFile(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            return file;
        }

        return null;
    }

    private static void DeleteTemp(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // A failed remove still returns the bytes already copied out.
        }
        catch (UnauthorizedAccessException)
        {
            // A failed remove still returns the bytes already copied out.
        }
    }
}
