using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.Launch;

/// <summary>Records what would start. Nothing real runs in these tests.</summary>
internal sealed class RecordingRunner : IProcessRunner
{
    public List<ProcessRequest> Started { get; } = [];

    public void Start(ProcessRequest request) => Started.Add(request);
}

internal sealed class LaunchHarness : IDisposable
{
    private LaunchHarness(string root, PanelWorkspace workspace, AppSettings settings)
    {
        Root = root;
        Workspace = workspace;
        Settings = settings;
        Left = Path.Combine(root, "left");
        Right = Path.Combine(root, "right");
        Text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        Runner = new RecordingRunner();
        Dialogs = new FakeDialogService();
        Commands = new LaunchCommands(workspace, Dialogs, Text, Runner, settings);
    }

    public string Root { get; }

    public string Left { get; }

    public string Right { get; }

    public PanelWorkspace Workspace { get; }

    public AppSettings Settings { get; }

    public ILocalizer Text { get; }

    public RecordingRunner Runner { get; }

    public FakeDialogService Dialogs { get; }

    public LaunchCommands Commands { get; }

    public static async Task<LaunchHarness> Open()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-launch-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "left", "sub"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var settings = new AppSettings(new SettingsFolder(Path.Combine(root, "config"), SettingsSource.Env));
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new IdlePicker());
        await workspace.OpenAsync(Path.Combine(root, "left"), Path.Combine(root, "right"), TestContext.Current.CancellationToken);
        return new LaunchHarness(root, workspace, settings);
    }

    public CommandLineModel NewLine() => new(Workspace, Runner, Dialogs, Text);

    public async Task AddFileAsync(string name)
    {
        await File.WriteAllTextAsync(Path.Combine(Left, name), "x", TestContext.Current.CancellationToken);
        await Workspace.RefreshAsync(TestContext.Current.CancellationToken);
    }

    public void Select(params string[] names)
    {
        Workspace.Activate(PanelSide.Left);
        var snap = Workspace.Read(PanelSide.Left);
        var marked = new string[names.Length];
        var cursor = 0;
        for (var i = 0; i < names.Length; i++)
        {
            marked[i] = Path.Combine(Left, names[i]);
            for (var e = 0; e < snap.Entries.Count; e++)
            {
                if (snap.Entries[e].Name == names[i])
                {
                    cursor = e;
                }
            }
        }

        Workspace.ApplySelection(PanelSide.Left, cursor, marked);
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Settings.Dispose();
        Directory.Delete(Root, true);
    }

    private sealed class IdlePicker : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
