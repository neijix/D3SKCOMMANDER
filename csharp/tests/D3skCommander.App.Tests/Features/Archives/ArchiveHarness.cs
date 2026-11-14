using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Archives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Archives;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.Archives;

internal sealed class ArchiveHarness : IDisposable
{
    private ArchiveHarness(string root, ILocalizer text, PanelWorkspace workspace, ArchiveCommands commands)
    {
        Root = root;
        Text = text;
        Workspace = workspace;
        Commands = commands;
        Left = Path.Combine(root, "left");
        Right = Path.Combine(root, "right");
    }

    public string Root { get; }

    public string Left { get; }

    public string Right { get; }

    public ILocalizer Text { get; }

    public PanelWorkspace Workspace { get; }

    public ArchiveCommands Commands { get; }

    public static async Task<ArchiveHarness> Open(FakeDialogService dialogs, IFileSystemProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var root = Directory.CreateTempSubdirectory("d3sk-archives-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var workspace = new PanelWorkspace(provider ?? new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(Path.Combine(root, "left"), Path.Combine(root, "right"), TestContext.Current.CancellationToken);
        var commands = new ArchiveCommands(workspace, dialogs, text, new ArchiveService(new JobService()));
        return new ArchiveHarness(root, text, workspace, commands);
    }

    public async Task AddFileAsync(string side, string name, string contents)
    {
        await File.WriteAllTextAsync(Path.Combine(side, name), contents, TestContext.Current.CancellationToken);
        await Workspace.RefreshAsync(TestContext.Current.CancellationToken);
    }

    public void Select(PanelSide side, params string[] names)
    {
        Workspace.Activate(side);
        var folder = side == PanelSide.Left ? Left : Right;
        var snap = Workspace.Read(side);
        var marked = new string[names.Length];
        var cursor = 0;
        for (var i = 0; i < names.Length; i++)
        {
            cursor = Index(snap, names[i]);
            marked[i] = Path.Combine(folder, names[i]);
        }

        Workspace.ApplySelection(side, cursor, marked);
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Directory.Delete(Root, true);
    }

    private static int Index(PanelSnapshot snap, string name)
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
}
