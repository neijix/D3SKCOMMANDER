using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.NetDrives;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.NetDrives;
using D3skCommander.Core.Providers;
using D3skCommander.Core.SettingsLocation;
using Xunit;

namespace D3skCommander.App.Tests.Features.NetDrives;

public class NetDrivesCommandTests
{
    [AvaloniaFact]
    public async Task ABadAddressIsExplainedAndNothingRuns()
    {
        using var harness = await Harness.Open();
        var dialogs = harness.Dialogs;
        var pending = harness.Commands.ConnectAsync();
        var shown = await dialogs.WaitShownAsync();
        var fields = shown.Form!.Fields;
        Assert.Contains(fields, field => field is TextDialogField { IsPassword: true });
        Assert.Contains(fields, field => field is TextDialogField { Label: "Drive letter (Windows)" });
        fields.OfType<TextDialogField>().First(field => field.Label == "Address").Text = "smb://server";
        dialogs.Answer(DialogResult.Ok());
        var message = await dialogs.WaitShownAsync();
        Assert.Contains("share name", message.Text, StringComparison.Ordinal);
        dialogs.Answer(DialogResult.Ok());
        await dialogs.WaitShownAsync();
        dialogs.Answer(DialogResult.Cancel());
        await pending;

        Assert.Empty(harness.Runner.Connects);
        Assert.Empty(harness.Settings.Session.Current.NetShares);
    }

    [AvaloniaFact]
    public async Task AGoodAddressMountsWithTheSecretOnStdinAndIsRemembered()
    {
        using var harness = await Harness.Open();
        var pending = harness.Commands.ConnectAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        var fields = shown.Form!.Fields.OfType<TextDialogField>().ToList();
        Assert.Equal("Password (not saved)", fields[3].Label);
        fields[0].Text = "\\\\nas\\media";
        fields[1].Text = "anna";
        fields[3].Text = "s3cret-pass";
        fields[4].Text = "z";
        harness.Dialogs.Answer(DialogResult.Ok());
        await pending;

        var call = Assert.Single(harness.Runner.Connects);
        Assert.Equal("nas", call.Share.Server);
        Assert.Equal("anna", call.Credentials.User);
        Assert.Equal("s3cret-pass", call.Credentials.Password);
        Assert.Equal("Z:", call.Letter);
        if (OperatingSystem.IsLinux())
        {
            var command = NetDrive.MountCommandFor(call.Share, call.Credentials);
            Assert.DoesNotContain("s3cret-pass", string.Join(' ', command.Arguments), StringComparison.Ordinal);
            Assert.Contains("s3cret-pass", command.StandardInput, StringComparison.Ordinal);
        }

        Assert.Equal(["\\\\nas\\media"], harness.Settings.Session.Current.NetShares);
        harness.Settings.Flush();
        Assert.DoesNotContain("s3cret-pass", File.ReadAllText(Path.Combine(harness.Settings.Directory, SettingsFileNames.Session)), StringComparison.Ordinal);
        Assert.Equal(harness.Mount, harness.Workspace.Read(PanelSide.Left).Path);
    }

    [AvaloniaFact]
    public async Task RecentAddressesAreOfferedAndFillTheAddress()
    {
        using var harness = await Harness.Open();
        var session = harness.Settings.Session.Current;
        session.NetShares = ["smb://old/share"];
        harness.Settings.Session.Save(session);
        var pending = harness.Commands.ConnectAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        var recent = Assert.IsType<ChoiceDialogField>(shown.Form!.Fields[0]);
        Assert.Equal("smb://old/share", recent.Options[1]);
        recent.SelectedIndex = 1;
        Assert.Equal("smb://old/share", shown.Form.Fields.OfType<TextDialogField>().First().Text);
        harness.Dialogs.Answer(DialogResult.Cancel());
        await pending;
    }

    [AvaloniaFact]
    public async Task DisconnectListsTheSharesAndUnmountsTheChosenOne()
    {
        using var harness = await Harness.Open();
        harness.Runner.Connected = [new ConnectedShare("smb://a/x", "/mnt/a"), new ConnectedShare("smb://b/y", "/mnt/b")];
        harness.Dialogs.Enqueue(DialogResult.Picked(1));
        await harness.Commands.DisconnectAsync();

        Assert.Equal(["/mnt/b"], harness.Runner.Disconnects);
    }

    [AvaloniaFact]
    public async Task AFailedMountIsReportedThroughErrorLines()
    {
        using var harness = await Harness.Open();
        harness.Runner.Failure = OperationError.FromMessage("connection refused");
        var pending = harness.Commands.ConnectAsync();
        var shown = await harness.Dialogs.WaitShownAsync();
        shown.Form!.Fields.OfType<TextDialogField>().First().Text = "smb://nas/media";
        harness.Dialogs.Answer(DialogResult.Ok());
        var message = await harness.Dialogs.WaitShownAsync();
        Assert.Equal(ErrorLines.Describe(harness.Text, harness.Runner.Failure), message.Text);
        harness.Dialogs.Answer(DialogResult.Ok());
        await pending;

        Assert.Empty(harness.Settings.Session.Current.NetShares);
    }

    private sealed class FakeRunner : INetDriveRunner
    {
        public List<(Share Share, Credentials Credentials, string? Letter)> Connects { get; } = [];

        public List<string> Disconnects { get; } = [];

        public IReadOnlyList<ConnectedShare> Connected { get; set; } = [];

        public OperationError? Failure { get; set; }

        public string Mount { get; set; } = "";

        public bool SupportsDriveLetter => true;

        public FileResult<string> Connect(Share share, Credentials credentials, string? letter)
        {
            Connects.Add((share, credentials, letter));
            return Failure is null ? FileResult<string>.Ok(Mount) : FileResult<string>.Fail(Failure);
        }

        public FileResult<string> Disconnect(string path)
        {
            Disconnects.Add(path);
            return FileResult<string>.Ok(path);
        }

        public IReadOnlyList<ConnectedShare> ListConnected() => Connected;
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _root;

        private Harness(string root, PanelWorkspace workspace, AppSettings settings, FakeRunner runner, FakeDialogService dialogs, ILocalizer text)
        {
            _root = root;
            Workspace = workspace;
            Settings = settings;
            Runner = runner;
            Dialogs = dialogs;
            Text = text;
            Mount = Path.Combine(root, "mount");
            runner.Mount = Mount;
            Commands = new NetDrivesCommands(workspace, dialogs, text, new JobService(), settings, runner);
        }

        public PanelWorkspace Workspace { get; }

        public AppSettings Settings { get; }

        public FakeRunner Runner { get; }

        public FakeDialogService Dialogs { get; }

        public ILocalizer Text { get; }

        public NetDrivesCommands Commands { get; }

        public string Mount { get; }

        public static async Task<Harness> Open()
        {
            var root = Directory.CreateTempSubdirectory("d3sk-net-").FullName;
            Directory.CreateDirectory(Path.Combine(root, "mount"));
            Directory.CreateDirectory(Path.Combine(root, "left"));
            Directory.CreateDirectory(Path.Combine(root, "cfg"));
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new Idle());
            await workspace.OpenAsync(Path.Combine(root, "left"), Path.Combine(root, "left"), TestContext.Current.CancellationToken);
            var settings = new AppSettings(new SettingsFolder(Path.Combine(root, "cfg"), SettingsSource.Default));
            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            return new Harness(root, workspace, settings, new FakeRunner(), new FakeDialogService(), text);
        }

        public void Dispose()
        {
            Workspace.Dispose();
            Settings.Dispose();
            Directory.Delete(_root, true);
        }
    }

    private sealed class Idle : IFolderPicker
    {
        public Task<string?> PickAsync(string? startFolder, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
