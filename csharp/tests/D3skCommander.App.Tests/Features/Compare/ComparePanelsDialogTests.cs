using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Compare;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.Compare;

public class ComparePanelsDialogTests
{
    [AvaloniaFact]
    public async Task ShiftF2MarksFilesThatDifferInBothPanels()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        var right = Path.Combine(root.Path, "right");
        Directory.CreateDirectory(Path.Combine(left, "folder"));
        Directory.CreateDirectory(Path.Combine(right, "folder"));
        var older = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Write(Path.Combine(left, "only-left.txt"), older, "L");
        Write(Path.Combine(right, "only-right.txt"), older, "R");
        Write(Path.Combine(left, "same.txt"), older.AddSeconds(3), "new");
        Write(Path.Combine(right, "same.txt"), older, "old");
        Write(Path.Combine(left, "close.txt"), older.AddSeconds(2), "a");
        Write(Path.Combine(right, "close.txt"), older, "b");

        using var workspace = await Open(left, right);
        var listed = workspace.Read(PanelSide.Left);
        workspace.ApplySelection(PanelSide.Left, At(listed, "close.txt"), [PathOf(listed, "close.txt")]);
        var dialogs = new RecordingDialogs();
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var commands = Commands(workspace, dialogs, text, new RememberTools());

        await commands.ComparePanelsAsync();

        Assert.Equal(text.Get("cmd.compare_dirs"), dialogs.Title);
        Assert.Equal("2 ← → 1", dialogs.Message);
        var leftMarks = workspace.Read(PanelSide.Left).Marked;
        var rightMarks = workspace.Read(PanelSide.Right).Marked;
        Assert.Equal(2, Named(leftMarks, "only-left.txt", "same.txt"));
        Assert.Equal(0, Named(leftMarks, "close.txt", "folder"));
        Assert.Equal(0, Extra(leftMarks, "only-left.txt", "same.txt"));
        Assert.Equal(1, Named(rightMarks, "only-right.txt"));
        Assert.Equal(0, Named(rightMarks, "same.txt", "close.txt"));
        Assert.Equal(0, Extra(rightMarks, "only-right.txt"));
    }

    [AvaloniaFact]
    public async Task CompareFilesOpensThePairFromEachPanel()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        var right = Path.Combine(root.Path, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        var leftFile = Path.Combine(left, "left.txt");
        var rightFile = Path.Combine(right, "right.txt");
        File.WriteAllText(leftFile, "a");
        File.WriteAllText(rightFile, "b");
        using var workspace = await Open(left, right);
        var tools = new RememberTools();
        var dialogs = new RecordingDialogs();
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var commands = Commands(workspace, dialogs, text, tools);
        new CompareModule(commands, tools).Contribute(new ShellContributions());

        commands.CompareFiles();

        var view = Assert.IsType<CompareView>(tools.View);
        Assert.Equal(leftFile, view.Session.LeftPath);
        Assert.Equal(rightFile, view.Session.RightPath);
        Assert.Equal("", dialogs.Message);
    }

    [AvaloniaFact]
    public async Task CompareFilesAsksWhenThePairIsMissing()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(Path.Combine(root.Path, "right"));
        File.WriteAllText(Path.Combine(left, "only.txt"), "a");
        using var workspace = await Open(left, Path.Combine(root.Path, "right"));
        var dialogs = new RecordingDialogs();
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var commands = Commands(workspace, dialogs, text, new RememberTools());

        commands.CompareFiles();

        Assert.Equal(text.Get("dlg.need_two_files"), dialogs.Message);
        Assert.Equal(text.Get("cmd.compare_files"), dialogs.Title);
    }

    private static CompareCommands Commands(PanelWorkspace workspace, RecordingDialogs dialogs, ILocalizer text, RememberTools tools) =>
        new(workspace, dialogs, text, new JobService(), tools);

    private static async Task<PanelWorkspace> Open(string left, string right)
    {
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(left, right, TestContext.Current.CancellationToken);
        return workspace;
    }

    private static void Write(string path, DateTime modified, string contents)
    {
        File.WriteAllText(path, contents);
        File.SetLastWriteTimeUtc(path, modified);
    }

    private static int Named(IReadOnlyList<string> paths, params string[] names)
    {
        var count = 0;
        foreach (var path in paths)
        {
            if (EndsWithAny(path, names))
            {
                count++;
            }
        }

        return count;
    }

    private static int Extra(IReadOnlyList<string> paths, params string[] names)
    {
        var count = 0;
        foreach (var path in paths)
        {
            if (!EndsWithAny(path, names))
            {
                count++;
            }
        }

        return count;
    }

    private static bool EndsWithAny(string path, string[] names)
    {
        foreach (var name in names)
        {
            if (path.EndsWith(name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int At(PanelSnapshot snap, string name)
    {
        for (var i = 0; i < snap.Entries.Count; i++)
        {
            if (snap.Entries[i].Name == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Missing " + name);
    }

    private static string PathOf(PanelSnapshot snap, string name)
    {
        foreach (var entry in snap.Entries)
        {
            if (entry.Name == name)
            {
                return entry.Path;
            }
        }

        throw new InvalidOperationException("Missing " + name);
    }

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public string Title { get; private set; } = "";

        public string Message { get; private set; } = "";

        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Title = title;
            Message = message;
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<string?> AskTextAsync(string title, string label, string initialText = "", CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices, CancellationToken cancellationToken = default) =>
            Task.FromResult<int?>(null);

        public Task<bool> ShowFormAsync(DialogForm form, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class RememberTools : IToolWindowHost
    {
        private Func<IShellWidget>? _build;

        public IShellWidget? View { get; private set; }

        public void Register(ToolKind kind, Func<IShellWidget> content) => _build = content;

        public ToolWindow Open(ToolKind kind)
        {
            View = _build?.Invoke();
            return null!;
        }

        public bool IsOpen(ToolKind kind) => false;

        public void Draw()
        {
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("d3sk-panels-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
