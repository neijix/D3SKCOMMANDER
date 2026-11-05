using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Providers;
using D3skCommander.Core.SettingsLocation;
using D3skCommander.Core.Trash;
using Xunit;

namespace D3skCommander.App.Tests.Features.FileOps;

public class FileOpsCommandTests
{
    [AvaloniaFact]
    public async Task CopyAsksForTheOtherPanelAndOverwritesWhenChosen()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        var right = Path.Combine(root.Path, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        File.WriteAllText(Path.Combine(left, "a.txt"), "new");
        File.WriteAllText(Path.Combine(right, "a.txt"), "old");
        var dialogs = new ScriptedDialogs { Choice = 0 };
        using var workspace = await Workspace(left, right);
        var commands = Commands(workspace, dialogs);
        var listed = workspace.Read(PanelSide.Left);
        workspace.ApplySelection(PanelSide.Left, At(listed, "a.txt"), []);

        await commands.CopyAsync();

        Assert.Equal("new", File.ReadAllText(Path.Combine(right, "a.txt")));
        Assert.IsType<FolderDialogField>(dialogs.Form!.Fields[0]);
        Assert.Equal(right, ((FolderDialogField)dialogs.Form.Fields[0]).Path);
        Assert.Contains(workspace.Read(PanelSide.Right).Entries, entry => entry.Name == "a.txt");
    }

    [AvaloniaFact]
    public async Task NewFolderAndDeleteChangeThePanel()
    {
        using var root = new TempDir();
        var left = Path.Combine(root.Path, "left");
        Directory.CreateDirectory(left);
        File.WriteAllText(Path.Combine(left, "gone.txt"), "x");
        var dialogs = new ScriptedDialogs { Text = "created", Confirm = true };
        using var workspace = await Workspace(left, left);
        var commands = Commands(workspace, dialogs);
        var listed = workspace.Read(PanelSide.Left);
        workspace.ApplySelection(PanelSide.Left, At(listed, "gone.txt"), []);

        await commands.NewFolderAsync();
        Assert.True(Directory.Exists(Path.Combine(left, "created")));

        listed = workspace.Read(PanelSide.Left);
        workspace.ApplySelection(PanelSide.Left, At(listed, "gone.txt"), []);
        await commands.DeleteAsync();

        Assert.False(File.Exists(Path.Combine(left, "gone.txt")));
        Assert.DoesNotContain(workspace.Read(PanelSide.Left).Entries, entry => entry.Name == "gone.txt");
    }

    [AvaloniaFact]
    public async Task ConflictAnswersApplyOneFileAtATime()
    {
        using var root = new TempDir();
        var (left, right) = Conflicting(root, "a.txt", "b.txt", "c.txt");
        var dialogs = new ScriptedDialogs();
        dialogs.Answers.Enqueue(0);
        dialogs.Answers.Enqueue(1);
        dialogs.Answers.Enqueue(0);
        using var workspace = await Workspace(left, right);
        var commands = Commands(workspace, dialogs);

        await CopyMarked(workspace, commands, "a.txt", "b.txt", "c.txt");

        Assert.Equal(3, dialogs.Asked.Count);
        Assert.Equal("new-a.txt", File.ReadAllText(Path.Combine(right, "a.txt")));
        Assert.Equal("old-b.txt", File.ReadAllText(Path.Combine(right, "b.txt")));
        Assert.Equal("new-c.txt", File.ReadAllText(Path.Combine(right, "c.txt")));
    }

    [AvaloniaFact]
    public async Task OverwriteAllStopsAskingAndOverwritesTheRest()
    {
        using var root = new TempDir();
        var (left, right) = Conflicting(root, "a.txt", "b.txt", "c.txt");
        var dialogs = new ScriptedDialogs();
        dialogs.Answers.Enqueue(3);
        using var workspace = await Workspace(left, right);
        var commands = Commands(workspace, dialogs);

        await CopyMarked(workspace, commands, "a.txt", "b.txt", "c.txt");

        Assert.Single(dialogs.Asked);
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            Assert.Equal("new-" + name, File.ReadAllText(Path.Combine(right, name)));
        }
    }

    [AvaloniaFact]
    public async Task SkipAllStopsAskingAndKeepsTheRest()
    {
        using var root = new TempDir();
        var (left, right) = Conflicting(root, "a.txt", "b.txt", "c.txt");
        var dialogs = new ScriptedDialogs();
        dialogs.Answers.Enqueue(4);
        using var workspace = await Workspace(left, right);
        var commands = Commands(workspace, dialogs);

        await CopyMarked(workspace, commands, "a.txt", "b.txt", "c.txt");

        Assert.Single(dialogs.Asked);
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            Assert.Equal("old-" + name, File.ReadAllText(Path.Combine(right, name)));
        }
    }

    [AvaloniaFact]
    public async Task AFailingItemShowsTheReportAndRetryRunsItAgain()
    {
        using var root = new TempDir();
        var (left, right) = Conflicting(root);
        File.WriteAllText(Path.Combine(left, "bad.txt"), "payload");
        File.WriteAllText(Path.Combine(left, "good.txt"), "fine");
        var dialogs = new ScriptedDialogs();
        dialogs.Answers.Enqueue(0);
        var provider = new FlakyProvider("bad.txt", failures: 1);
        using var workspace = await Workspace(left, right);
        var commands = Commands(workspace, dialogs, provider);

        await CopyMarked(workspace, commands, "bad.txt", "good.txt");

        var report = Assert.Single(dialogs.Asked);
        Assert.Equal("Some items failed", report.Title);
        Assert.Contains("disk says no", report.Message, StringComparison.Ordinal);
        Assert.Equal(["Retry", "Skip"], report.Choices);
        Assert.Equal("payload", File.ReadAllText(Path.Combine(right, "bad.txt")));
        Assert.Equal("fine", File.ReadAllText(Path.Combine(right, "good.txt")));
        Assert.Contains(workspace.Read(PanelSide.Right).Entries, entry => entry.Name == "bad.txt");
    }

    [AvaloniaFact]
    public async Task SkippingAFailureLeavesTheItemUndone()
    {
        using var root = new TempDir();
        var (left, right) = Conflicting(root);
        File.WriteAllText(Path.Combine(left, "bad.txt"), "payload");
        File.WriteAllText(Path.Combine(left, "good.txt"), "fine");
        var dialogs = new ScriptedDialogs();
        dialogs.Answers.Enqueue(1);
        var provider = new FlakyProvider("bad.txt", failures: 1);
        using var workspace = await Workspace(left, right);
        var commands = Commands(workspace, dialogs, provider);

        await CopyMarked(workspace, commands, "bad.txt", "good.txt");

        Assert.Single(dialogs.Asked);
        Assert.Equal(1, provider.Failed);
        Assert.False(File.Exists(Path.Combine(right, "bad.txt")));
        Assert.Equal("fine", File.ReadAllText(Path.Combine(right, "good.txt")));
    }

    private static (string Left, string Right) Conflicting(TempDir root, params string[] names)
    {
        var left = Path.Combine(root.Path, "left");
        var right = Path.Combine(root.Path, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(left, name), "new-" + name);
            File.WriteAllText(Path.Combine(right, name), "old-" + name);
        }

        return (left, right);
    }

    private static async Task CopyMarked(PanelWorkspace workspace, FileOpsCommands commands, params string[] names)
    {
        var listed = workspace.Read(PanelSide.Left);
        var paths = names.Select(name => listed.Entries[At(listed, name)].Path).ToList();
        workspace.ApplySelection(PanelSide.Left, At(listed, names[0]), paths);
        await commands.CopyAsync();
    }

    internal static FileOpsCommands Commands(
        PanelWorkspace workspace,
        ScriptedDialogs dialogs,
        IFileSystemProvider? provider = null,
        AppSettings? settings = null,
        ITrash? trash = null)
    {
        var jobs = new JobService();
        var files = new FileOperationService(jobs, new OperationLog());
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var prefs = settings ?? Settings(true, true);
        return new FileOpsCommands(
            workspace,
            files,
            dialogs,
            text,
            jobs,
            provider ?? new LocalFileSystemProvider(),
            prefs,
            trash ?? new FakeTrash());
    }

    internal static async Task<PanelWorkspace> Workspace(string left, string right)
    {
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(left, right, TestContext.Current.CancellationToken);
        return workspace;
    }

    internal static int At(PanelSnapshot snap, string name)
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

    internal sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    internal sealed class ScriptedDialogs : IDialogService
    {
        public int? Choice { get; set; }

        public Queue<int?> Answers { get; } = new();

        public List<(string Title, string Message, string[] Choices)> Asked { get; } = [];

        public string? Text { get; set; }

        public bool Confirm { get; set; } = true;

        public DialogForm? Form { get; private set; }

        public List<string> Confirmed { get; } = [];

        public List<string> Messages { get; } = [];

        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            Confirmed.Add(message);
            return Task.FromResult(Confirm);
        }

        public Task<string?> AskTextAsync(
            string title,
            string label,
            string initialText = "",
            CancellationToken cancellationToken = default) => Task.FromResult(Text);

        public Task<int?> ChooseAsync(
            string title,
            string message,
            IReadOnlyList<string> choices,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((title, message, [.. choices]));
            return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue() : Choice);
        }

        public Task<bool> ShowFormAsync(DialogForm form, CancellationToken cancellationToken = default)
        {
            Form = form;
            return Task.FromResult(true);
        }
    }

    /// <summary>The local provider, except that writing a file with the given name fails the first times.</summary>
    private sealed class FlakyProvider : IFileSystemProvider
    {
        private readonly LocalFileSystemProvider _inner = new();
        private readonly string _name;
        private int _failuresLeft;

        public FlakyProvider(string name, int failures)
        {
            _name = name;
            _failuresLeft = failures;
        }

        public int Failed { get; private set; }

        public bool IsReadOnly => _inner.IsReadOnly;

        public IAsyncEnumerable<FileResult<Entry>> ListAsync(string directory, bool showHidden, CancellationToken cancellationToken) =>
            _inner.ListAsync(directory, showHidden, cancellationToken);

        public Task<FileResult<Entry>> GetEntryAsync(string path, CancellationToken cancellationToken) =>
            _inner.GetEntryAsync(path, cancellationToken);

        public Task<FileResult<Stream>> OpenReadAsync(string path, CancellationToken cancellationToken) =>
            _inner.OpenReadAsync(path, cancellationToken);

        public Task<FileResult<Stream>> OpenWriteAsync(string path, CancellationToken cancellationToken)
        {
            if (Path.GetFileName(path).Contains(_name, StringComparison.Ordinal) && _failuresLeft > 0)
            {
                _failuresLeft--;
                Failed++;
                return Task.FromResult(FileResult<Stream>.Fail(OperationError.AccessDenied("disk says no", path)));
            }

            return _inner.OpenWriteAsync(path, cancellationToken);
        }

        public Task<FileStatus> CreateFolderAsync(string path, CancellationToken cancellationToken) =>
            _inner.CreateFolderAsync(path, cancellationToken);

        public Task<FileResult<string>> RenameAsync(string path, string newName, CancellationToken cancellationToken) =>
            _inner.RenameAsync(path, newName, cancellationToken);

        public Task<FileStatus> DeleteAsync(string path, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(path, cancellationToken);
    }

    internal static AppSettings Settings(bool useTrash, bool confirm)
    {
        var dir = Directory.CreateTempSubdirectory("d3sk-fo-cfg-").FullName;
        var settings = new AppSettings(new SettingsFolder(dir, SettingsSource.Env));
        var current = settings.Preferences.Current;
        current.UseTrash = useTrash;
        current.ConfirmDelete = confirm;
        settings.Preferences.Save(current);
        return settings;
    }

    internal sealed class FakeTrash : ITrash
    {
        public List<string> Sent { get; } = [];

        public string? FailName { get; set; }

        public bool Unavailable { get; set; }

        public TrashStatus MoveToTrash(string path)
        {
            if (Unavailable)
            {
                return TrashStatus.Unavailable(path);
            }

            if (FailName is not null && Path.GetFileName(path) == FailName)
            {
                return TrashStatus.Fail(OperationError.AccessDenied("no", path));
            }

            Sent.Add(path);
            File.Delete(path);
            return TrashStatus.Success;
        }
    }

    internal sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("d3sk-ops-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
