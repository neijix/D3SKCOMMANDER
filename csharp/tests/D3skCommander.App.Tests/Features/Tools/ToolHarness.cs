using Avalonia.Controls;
using Avalonia.VisualTree;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Tools;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Checksums;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Splitter;
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

    /// <summary>Renders the form through the shared template; fails if a field type has no editor.</summary>
    public static void Draw(DialogForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        using var shown = Render(form);
        Assert.NotEmpty(shown.View.GetVisualDescendants().OfType<Control>());
    }

    public static void Browse(DialogRequest request, ILocalizer text, string chosen)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(text);
        var form = request.Form ?? throw new InvalidOperationException("A tool dialog needs a form.");
        var picker = new FakeFolderPicker();
        picker.Enqueue(chosen);
        DialogWindowModel.Create(request, text, picker);
        using var shown = Render(form);
        var button = Assert.Single(
            shown.View.GetVisualDescendants().OfType<Button>(),
            candidate => Equals(candidate.Content, text.Get("dialog.browse")));
        Assert.True(button.Command!.CanExecute(null));
        button.Command.Execute(null);
        Assert.Equal(chosen, Assert.Single(form.Fields.OfType<FolderDialogField>()).Path);
    }

    private static RenderedForm Render(DialogForm form)
    {
        var view = new FormDialogView { DataContext = form };
        var window = new Window { Content = view };
        window.Show();
        return new RenderedForm(window, view);
    }

    private sealed class RenderedForm(Window window, FormDialogView view) : IDisposable
    {
        public FormDialogView View { get; } = view;

        public void Dispose() => window.Close();
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
