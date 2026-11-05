using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Drives;
using D3skCommander.App.Features.NetDrives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.FileOps;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.OperationLog;
using D3skCommander.Core.Providers;
using Xunit;

namespace D3skCommander.App.Tests.Features.Drives;

/// <summary>Two temp folders in the panels, a fixed drive list and one share, and scripted dialogs.</summary>
internal sealed class DrivesHarness : IDisposable
{
    private readonly string _root;

    private DrivesHarness(string root, PanelWorkspace workspace, FakeDialogService dialogs, ILocalizer text)
    {
        _root = root;
        Workspace = workspace;
        Dialogs = dialogs;
        Text = text;
        var jobs = new JobService();
        Commands = new DrivesCommands(
            workspace,
            dialogs,
            text,
            jobs,
            new FixedDrives(),
            new FixedShares(),
            new FileOperationService(jobs, new OperationLog()),
            new LocalFileSystemProvider());
        Commands.Bind(Shell);
    }

    public PanelWorkspace Workspace { get; }

    public FakeDialogService Dialogs { get; }

    public ILocalizer Text { get; }

    public DrivesCommands Commands { get; }

    public ShellContributions Shell { get; } = new();

    public string Left => Path.Combine(_root, "left");

    public string Right => Path.Combine(_root, "right");

    public string Media => Path.Combine(_root, "media");

    public static async Task<DrivesHarness> Open()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-drives-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "left"));
        Directory.CreateDirectory(Path.Combine(root, "right"));
        Directory.CreateDirectory(Path.Combine(root, "media"));
        var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Idle());
        await workspace.OpenAsync(Path.Combine(root, "left"), Path.Combine(root, "right"), TestContext.Current.CancellationToken);
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        return new DrivesHarness(root, workspace, new FakeDialogService(), text);
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Directory.Delete(_root, true);
    }

    private sealed class FixedDrives : IDriveSource
    {
        public IReadOnlyList<DriveEntry> Mounted() => [new DriveEntry("/", "/", "ext4", 1024, 4096)];
    }

    private sealed class FixedShares : INetDriveRunner
    {
        public bool SupportsDriveLetter => false;

        public FileResult<string> Connect(Share share, Credentials credentials, string? letter) =>
            throw new NotSupportedException();

        public FileResult<string> Disconnect(string path) => throw new NotSupportedException();

        public IReadOnlyList<ConnectedShare> ListConnected() => [new ConnectedShare("smb://nas/media", "/mnt/nas")];
    }

    private sealed class Idle : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
