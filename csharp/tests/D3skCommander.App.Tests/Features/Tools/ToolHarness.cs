using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Tools;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Checksums;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Splitter;
using Hexa.NET.ImGui;
using Xunit;

namespace D3skCommander.App.Tests.Features.Tools;

internal sealed class ToolHarness : IDisposable
{
    private readonly string _root;

    private ToolHarness(
        string root,
        JobService jobs,
        ILocalizer text,
        PanelWorkspace workspace,
        ToolsCommands commands)
    {
        _root = root;
        Jobs = jobs;
        Text = text;
        Workspace = workspace;
        Commands = commands;
        Left = Path.Combine(root, "left");
        Right = Path.Combine(root, "right");
    }

    public JobService Jobs { get; }

    public ILocalizer Text { get; }

    public PanelWorkspace Workspace { get; }

    public ToolsCommands Commands { get; }

    public string Left { get; }

    public string Right { get; }

    public string Root => _root;

    public static async Task<ToolHarness> Open(FakeDialogService dialogs, Language language = Language.En)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var root = Directory.CreateTempSubdirectory("d3sk-tools-").FullName;
        var left = Path.Combine(root, "left");
        var right = Path.Combine(root, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        var jobs = new JobService();
        var text = Localizer.Load(settingsLanguage: language, systemLanguage: language);
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(left, right, TestContext.Current.CancellationToken);
        var commands = new ToolsCommands(
            workspace,
            dialogs,
            text,
            jobs,
            new SplitterService(jobs),
            new ChecksumService(jobs));
        return new ToolHarness(root, jobs, text, workspace, commands);
    }

    public async Task AddFileAsync(string name, string contents)
    {
        var path = Path.Combine(Left, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents, TestContext.Current.CancellationToken);
        await RefreshAsync();
    }

    public Task RefreshAsync() => Workspace.RefreshAsync(TestContext.Current.CancellationToken);

    public void Select(PanelSide side, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
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

    public static void Draw(DialogForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        DrawView(new FormDialogView(form));
    }

    public static void Browse(DialogRequest request, ILocalizer text, string chosen)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(text);
        var form = request.Form ?? throw new InvalidOperationException("A tool dialog needs a form.");
        var picker = new FakeFolderPicker();
        picker.Enqueue(chosen);
        DialogWindowModel.Create(request, text, picker);
        var view = new FormDialogView(form);
        DrawView(view);

        var button = Assert.Single(view.BrowseButtons);
        Assert.Equal(text.Get("dialog.browse"), button.Content);
        Assert.True(button.Command.CanExecute(null));
        button.Command.Execute(null);
        Assert.Equal(chosen, Assert.Single(form.Fields.OfType<FolderDialogField>()).Path);
    }

    private static void DrawView(FormDialogView view)
    {
        ImGuiProbe.Draw(() =>
        {
            ImGui.Begin("tool-form");
            try
            {
                view.Draw();
            }
            finally
            {
                ImGui.End();
            }
        });
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Directory.Delete(_root, true);
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
