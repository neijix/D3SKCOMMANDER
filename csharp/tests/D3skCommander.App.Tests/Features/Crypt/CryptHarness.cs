using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Crypt;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Crypt;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.Crypt;

internal sealed class CryptHarness : IDisposable
{
    private CryptHarness(string root, FakeDialogService dialogs, PanelWorkspace workspace, ILocalizer text, JobService jobs)
    {
        Root = root;
        Dialogs = dialogs;
        Workspace = workspace;
        Text = text;
        Jobs = jobs;
        Left = Path.Combine(root, "left");
        Right = Path.Combine(root, "right");
        VaultPath = Path.Combine(root, PasswordStore.FileName);
        Options = new CryptOptions(EncryptionCost.Minimum);
        Vault = NewVault();
        Commands = new CryptCommands(workspace, dialogs, text, jobs, Vault, new NoTools(), Options);
    }

    public string Root { get; }

    public string Left { get; }

    public string Right { get; }

    public string VaultPath { get; }

    public FakeDialogService Dialogs { get; }

    public PanelWorkspace Workspace { get; }

    public ILocalizer Text { get; }

    public JobService Jobs { get; }

    public CryptOptions Options { get; }

    public PasswordVault Vault { get; }

    public CryptCommands Commands { get; }

    public static async Task<CryptHarness> Open()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-crypt-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(Path.Combine(root, "left"), Path.Combine(root, "right"), TestContext.Current.CancellationToken);
        return new CryptHarness(root, new FakeDialogService(), workspace, text, new JobService());
    }

    /// <summary>A second vault over the same file: a new session.</summary>
    public PasswordVault NewVault() => new(VaultPath, Dialogs, Text, Options);

    public async Task AddFileAsync(string name, string contents)
    {
        await File.WriteAllTextAsync(Path.Combine(Left, name), contents, TestContext.Current.CancellationToken);
        await Workspace.RefreshAsync(TestContext.Current.CancellationToken);
    }

    public void SelectLeft(string name)
    {
        Workspace.Activate(PanelSide.Left);
        var snap = Workspace.Read(PanelSide.Left);
        var cursor = 0;
        for (var i = 0; i < snap.Entries.Count; i++)
        {
            if (snap.Entries[i].Name == name)
            {
                cursor = i;
            }
        }

        Workspace.ApplySelection(PanelSide.Left, cursor, [Path.Combine(Left, name)]);
    }

    /// <summary>Waits for the next dialog, lets <paramref name="fill"/> edit its form, and accepts it.</summary>
    public async Task<DialogForm> AcceptFormAsync(Action<DialogForm> fill)
    {
        var shown = await Dialogs.WaitShownAsync();
        var form = shown.Form ?? throw new InvalidOperationException("A form was expected.");
        fill(form);
        Dialogs.Answer(DialogResult.Ok());
        return form;
    }

    public async Task<DialogRequest> AcceptMessageAsync()
    {
        var shown = await Dialogs.WaitShownAsync();
        Dialogs.Answer(DialogResult.Ok());
        return shown;
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Directory.Delete(Root, true);
    }

    private sealed class NoTools : IToolWindowHost
    {
        public void Register(ToolKind kind, Func<IShellWidget> content)
        {
        }

        public ToolWindow Open(ToolKind kind) => throw new NotSupportedException();

        public bool IsOpen(ToolKind kind) => false;
    }

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
