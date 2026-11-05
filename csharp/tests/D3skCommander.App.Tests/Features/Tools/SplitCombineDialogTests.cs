using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.Tools;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Tools;

public class SplitCombineDialogTests
{
    [Fact]
    public async Task SplitAsksForAFolderAndWritesTheParts()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("blob.bin", "0123456789abcdef");
        harness.Select(PanelSide.Left, "blob.bin");

        var pending = harness.Commands.SplitAsync();
        var shown = await dialogs.WaitShownAsync();
        var form = shown.Form!;
        Assert.Equal(harness.Text.Get("cmd.split"), form.Title);
        Assert.Equal(Path.Combine(harness.Left, "blob.bin"), form.Fields[0].Label);
        var dest = Assert.IsType<FolderDialogField>(form.Fields[1]);
        Assert.Equal(harness.Text.Get("target"), dest.Label);
        Assert.Equal(harness.Right, dest.Path);
        var size = Assert.IsType<TextDialogField>(form.Fields[2]);
        Assert.Equal("100M", size.Text);
        size.Text = "nope";
        Assert.False(form.CanAccept);
        size.Text = "1.44M";
        Assert.True(form.CanAccept);
        size.Text = "8";
        var picked = Path.Combine(harness.Root, "picked");
        Directory.CreateDirectory(picked);
        ToolHarness.Browse(shown, harness.Text, picked);
        dialogs.Answer(DialogResult.Ok());
        await pending;

        Assert.True(File.Exists(Path.Combine(picked, "blob.bin.001")));
        Assert.True(File.Exists(Path.Combine(picked, "blob.bin.002")));
        Assert.True(File.Exists(Path.Combine(picked, "blob.bin.crc")));
        Assert.Equal("0123456789abcdef", await File.ReadAllTextAsync(Path.Combine(harness.Left, "blob.bin"), TestContext.Current.CancellationToken));
        Assert.NotEmpty(harness.Jobs.Jobs);
    }

    [Fact]
    public async Task CombineJoinsThePartsIntoTheChosenFolder()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        const string Original = "0123456789abcdef";
        await harness.AddFileAsync("blob.bin", Original);
        harness.Select(PanelSide.Left, "blob.bin");
        var split = harness.Commands.SplitAsync();
        var splitShown = await dialogs.WaitShownAsync();
        Assert.IsType<TextDialogField>(splitShown.Form!.Fields[2]).Text = "8";
        dialogs.Answer(DialogResult.Ok());
        await split;

        harness.Select(PanelSide.Right, "blob.bin.001");
        var pending = harness.Commands.CombineAsync();
        var shown = await dialogs.WaitShownAsync();
        var form = shown.Form!;
        Assert.Equal(harness.Text.Get("cmd.combine"), form.Title);
        Assert.IsType<FolderDialogField>(Assert.Single(form.Fields));
        var joined = Path.Combine(harness.Root, "joined");
        Directory.CreateDirectory(joined);
        ToolHarness.Browse(shown, harness.Text, joined);
        dialogs.Answer(DialogResult.Ok());
        await pending;

        Assert.Equal(Original, await File.ReadAllTextAsync(Path.Combine(joined, "blob.bin"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelAndAFolderDoNotSplit()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("blob.bin", "0123456789abcdef");
        harness.Select(PanelSide.Left, "blob.bin");
        var pending = harness.Commands.SplitAsync();
        await dialogs.WaitShownAsync();
        dialogs.Answer(DialogResult.Cancel());
        await pending;
        Assert.Empty(harness.Jobs.Jobs);
        Assert.False(File.Exists(Path.Combine(harness.Right, "blob.bin.001")));

        Directory.CreateDirectory(Path.Combine(harness.Left, "sub"));
        await harness.RefreshAsync();
        harness.Select(PanelSide.Left, "sub");
        pending = harness.Commands.SplitAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, shown.Kind);
        Assert.Equal(harness.Text.Get("dlg.no_selection"), shown.Text);
        dialogs.Answer(DialogResult.Ok());
        await pending;
    }

    [Fact]
    public async Task ContributeBindsTheToolCommands()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        var shell = new ShellContributions();
        new ToolsModule(harness.Commands).Contribute(shell);
        Assert.True(shell.CanExecute(CommandId.Split));
        Assert.True(shell.CanExecute(CommandId.Combine));
        Assert.True(shell.CanExecute(CommandId.ChecksumCreate));
        Assert.True(shell.CanExecute(CommandId.ChecksumVerify));
        Assert.True(shell.CanExecute(CommandId.AddTime));
        Assert.True(shell.CanExecute(CommandId.ReDate));
        Assert.True(shell.CanExecute(CommandId.Touch));

        var waiting = dialogs.WaitShownAsync();
        Assert.True(shell.Dispatch(CommandId.Split));
        var shown = await waiting;
        Assert.Equal(DialogKind.Message, shown.Kind);
        Assert.Equal(harness.Text.Get("dlg.no_selection"), shown.Text);
        dialogs.Answer(DialogResult.Ok());
        for (var i = 0; i < 50 && dialogs.Current is not null; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Null(dialogs.Current);
    }

    [Fact]
    public async Task RegisterExposesTheModuleAsAShellFeature()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        var services = new ServiceCollection();
        services.AddSingleton<IPanelWorkspace>(harness.Workspace);
        services.AddSingleton<IDialogService>(dialogs);
        services.AddSingleton(harness.Text);
        services.AddSingleton<IJobService>(harness.Jobs);
        ToolsModule.Register(services);
        using var provider = services.BuildServiceProvider();
        var module = provider.GetRequiredService<ToolsModule>();
        Assert.Same(module, Assert.Single(provider.GetServices<IShellModule>()));
    }

    [Fact]
    public void ToolStringsAreBilingual()
    {
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        Assert.Equal("One file per file", text.Get("tools.one_per_file", Language.En));
        Assert.Equal("Un fichier par fichier", text.Get("tools.one_per_file", Language.Fr));
        Assert.NotEqual(text.Get("tools.one_per_file", Language.En), text.Get("tools.one_per_file", Language.Fr));
        Assert.NotEqual(text.Get("tools.select_checksum", Language.En), text.Get("tools.select_checksum", Language.Fr));
        Assert.NotEqual(text.Get("tools.differs", Language.En), text.Get("tools.differs", Language.Fr));
        Assert.NotEqual(text.Get("tools.missing", Language.En), text.Get("tools.missing", Language.Fr));
        Assert.NotEqual(text.Get("tools.problems", Language.En), text.Get("tools.problems", Language.Fr));
        Assert.Contains(".sha256", text.Get("tools.select_checksum", Language.En));
    }

    [Fact]
    public async Task TheSplitTitleFollowsTheLanguage()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs, Language.Fr);
        await harness.AddFileAsync("blob.bin", "x");
        harness.Select(PanelSide.Left, "blob.bin");
        var pending = harness.Commands.SplitAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal("Découper un fichier…", shown.Form!.Title);
        Assert.Equal(harness.Text.Get("target"), Assert.IsType<FolderDialogField>(shown.Form.Fields[1]).Label);
        dialogs.Answer(DialogResult.Cancel());
        await pending;
    }
}
