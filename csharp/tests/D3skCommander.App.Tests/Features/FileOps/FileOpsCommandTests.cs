using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.FileOps;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Providers;
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

    private static FileOpsCommands Commands(PanelWorkspace workspace, ScriptedDialogs dialogs)
    {
        var jobs = new JobService();
        var files = new FileOperationService(jobs, new OperationLog());
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        return new FileOpsCommands(workspace, files, dialogs, text, jobs, new LocalFileSystemProvider());
    }

    private static async Task<PanelWorkspace> Workspace(string left, string right)
    {
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(left, right, TestContext.Current.CancellationToken);
        return workspace;
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

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ScriptedDialogs : IDialogService
    {
        public int? Choice { get; set; }

        public string? Text { get; set; }

        public bool Confirm { get; set; } = true;

        public DialogForm? Form { get; private set; }

        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default) =>
            Task.FromResult(Confirm);

        public Task<string?> AskTextAsync(
            string title,
            string label,
            string initialText = "",
            CancellationToken cancellationToken = default) => Task.FromResult(Text);

        public Task<int?> ChooseAsync(
            string title,
            string message,
            IReadOnlyList<string> choices,
            CancellationToken cancellationToken = default) => Task.FromResult(Choice);

        public Task<bool> ShowFormAsync(DialogForm form, CancellationToken cancellationToken = default)
        {
            Form = form;
            return Task.FromResult(true);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("d3sk-ops-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
