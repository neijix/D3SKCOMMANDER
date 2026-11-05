using System.Runtime.CompilerServices;
using D3skCommander.Core.Archives;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Providers;

public sealed partial class ArchiveFileSystemProvider
{
    protected override async IAsyncEnumerable<FileResult<Entry>> ListCore(
        string directory,
        bool showHidden,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var loaded = Load(directory);
        if (loaded.Error is not null)
        {
            yield return FileResult<Entry>.Fail(loaded.Error);
            yield break;
        }

        foreach (var entry in loaded.Entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield return FileResult<Entry>.Fail(OperationError.Cancelled(directory));
                yield break;
            }

            if (showHidden || !entry.Hidden)
            {
                yield return FileResult<Entry>.Ok(entry);
            }
        }
    }

    protected override Task<FileResult<Entry>> GetEntryCore(string path, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FileResult<Entry>.Fail(OperationError.Cancelled(path)));
        }

        var place = Locate(path);
        if (place.Error is not null || place.Archive is null)
        {
            return Task.FromResult(FileResult<Entry>.Fail(place.Error ?? OperationError.NotFound(path)));
        }

        if (place.Inner.Length == 0)
        {
            return Task.FromResult(FileResult<Entry>.Ok(ArchiveRoot(place.Archive)));
        }

        var listed = ArchiveIo.List(place.Archive);
        if (!listed.IsSuccess)
        {
            return Task.FromResult(FileResult<Entry>.Fail(listed.Error ?? OperationError.NotFound(path)));
        }

        var member = Find(listed.Value, place.Inner);
        if (member is null)
        {
            return Task.FromResult(FileResult<Entry>.Fail(OperationError.NotFound(path)));
        }

        return Task.FromResult(FileResult<Entry>.Ok(ToEntry(place.Archive, member)));
    }

    private static Listed Load(string directory)
    {
        var place = Locate(directory);
        if (place.Error is not null || place.Archive is null)
        {
            return Listed.Fail(place.Error ?? OperationError.NotFound(directory));
        }

        var listed = ArchiveIo.List(place.Archive);
        if (!listed.IsSuccess)
        {
            return Listed.Fail(listed.Error ?? OperationError.NotFound(directory));
        }

        if (place.Inner.Length > 0 && !IsFolder(listed.Value, place.Inner))
        {
            return Listed.Fail(OperationError.NotFound(directory));
        }

        return Listed.Ok(Children(place, listed.Value));
    }

    private static List<Entry> Children(ArchivePlace place, IReadOnlyList<ArchiveEntry> entries)
    {
        var found = new List<Entry>();
        AddParent(place, found);
        foreach (var child in ArchiveIo.ListDirectory(entries, place.Inner))
        {
            found.Add(ToEntry(place.Archive!, child));
        }

        return found;
    }

    private static void AddParent(ArchivePlace place, List<Entry> found)
    {
        var parent = ParentOf(place);
        if (parent is null)
        {
            return;
        }

        found.Add(new Entry("..", parent, true, false, true, 0, null, false, false, ""));
    }

    private static string? ParentOf(ArchivePlace place)
    {
        if (place.Archive is null)
        {
            return null;
        }

        if (place.Inner.Length == 0)
        {
            var parent = Path.GetDirectoryName(place.Archive);
            return string.IsNullOrEmpty(parent) ? null : parent;
        }

        var slash = place.Inner.LastIndexOf('/');
        var parentInner = slash < 0 ? "" : place.Inner[..slash];
        return MemberPath(place.Archive, parentInner);
    }

    private static Entry ArchiveRoot(string archive) =>
        new(Path.GetFileName(archive), archive, true, false, false, 0, null, false, true, "");

    private static Entry ToEntry(string archive, ArchiveEntry child)
    {
        var name = NameOf(child.Path);
        var directory = child.IsDirectory;
        return new Entry(
            name,
            MemberPath(archive, child.Path),
            directory,
            false,
            false,
            directory ? 0 : (ulong)child.Size,
            child.Modified,
            IsDotName(name),
            true,
            "");
    }

    private static bool IsFolder(IReadOnlyList<ArchiveEntry> entries, string inner)
    {
        var exact = Find(entries, inner);
        if (exact is not null)
        {
            return exact.IsDirectory;
        }

        var prefix = inner + "/";
        foreach (var entry in entries)
        {
            if (entry.Path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ArchiveEntry? Find(IReadOnlyList<ArchiveEntry> entries, string inner)
    {
        foreach (var entry in entries)
        {
            if (entry.Path == inner)
            {
                return entry;
            }
        }

        return null;
    }

    private static string NameOf(string member)
    {
        var slash = member.LastIndexOf('/');
        return slash < 0 ? member : member[(slash + 1)..];
    }

    private static bool IsDotName(string name) => name.StartsWith('.') && name != "..";

    private readonly struct Listed
    {
        private Listed(OperationError? error, List<Entry>? entries)
        {
            Error = error;
            Entries = entries ?? [];
        }

        public OperationError? Error { get; }

        public List<Entry> Entries { get; }

        public static Listed Ok(List<Entry> entries) => new(null, entries);

        public static Listed Fail(OperationError error) => new(error, null);
    }
}
