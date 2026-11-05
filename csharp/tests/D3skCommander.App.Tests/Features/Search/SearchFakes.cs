using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Providers;

namespace D3skCommander.App.Tests.Features.Search;

internal sealed class SearchDir : IDisposable
{
    public SearchDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "d3sk-search-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Write(string relative, string text)
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
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

internal sealed class SearchPanels(string activePath) : IPanelWorkspace
{
    public List<(PanelSide Side, string Path)> Visits { get; } = [];

    public PanelSide ActiveSide => PanelSide.Right;

    public PanelSnapshot Read(PanelSide side) => PanelSnapshot.Empty with { Path = activePath };

    public PanelCapture? Capture() => null;

    public Task<bool> GoAsync(PanelSide side, string path, CancellationToken cancellationToken = default)
    {
        Visits.Add((side, path));
        return Task.FromResult(true);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class SearchPicker(string? answer) : IFolderPicker
{
    public string? StartedAt { get; private set; }

    public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default)
    {
        StartedAt = startFolder;
        return Task.FromResult(answer);
    }
}

internal sealed class SearchTools : IToolWindowHost
{
    public List<ToolKind> Registered { get; } = [];

    public Func<IShellWidget>? Factory { get; private set; }

    public bool Opened { get; private set; }

    public void Register(ToolKind kind, Func<IShellWidget> content)
    {
        Registered.Add(kind);
        Factory = content;
    }

    public ToolWindow Open(ToolKind kind)
    {
        Opened = kind == ToolKind.Search;
        return null!;
    }

    public bool IsOpen(ToolKind kind) => Opened;
}

/// <summary>A folder that yields one file and then waits until the search is cancelled.</summary>
internal sealed class StallingProvider : IFileSystemProvider
{
    public bool IsReadOnly => true;

    public async IAsyncEnumerable<FileResult<Entry>> ListAsync(
        string directory,
        bool showHidden,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return FileResult<Entry>.Ok(new Entry("first.txt", "/stall/first.txt", false, false, false, 1, null, false, false, ""));
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
    }

    public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<Entry>.Ok(new Entry("stall", path, true, false, false, 0, null, false, false, "")));

    public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(FileResult<Stream>.Fail(OperationError.Unsupported("none")));

    public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
