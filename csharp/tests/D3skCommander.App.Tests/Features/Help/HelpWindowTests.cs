using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Help;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Help;

public class HelpWindowTests
{
    [AvaloniaFact]
    public void KeysListsEveryCommandWithItsGesture()
    {
        using var host = ShellHost.Open(Language.En);
        var text = host.Localizer;

        var model = new KeysModel(text);

        Assert.Equal(CommandCatalog.All.Count, model.Rows.Count);
        foreach (var command in CommandCatalog.All)
        {
            var row = model.Rows.Single(r => r.Command == command.ToString());
            Assert.Equal(text.Get(CommandCatalog.LabelKey(command)), row.Label);
        }

        Assert.Equal("F5", model.Rows.Single(r => r.Command == nameof(CommandId.Copy)).Shortcuts);
        Assert.Contains("Delete", model.Rows.Single(r => r.Command == nameof(CommandId.Delete)).Shortcuts, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TheFilterNarrowsTheRows()
    {
        using var host = ShellHost.Open(Language.En);
        var model = new KeysModel(host.Localizer);

        model.Filter = "F5";

        Assert.NotEmpty(model.Rows);
        Assert.True(model.Rows.Count < CommandCatalog.All.Count);
        Assert.All(model.Rows, row => Assert.Contains("F5", row.Shortcuts, StringComparison.Ordinal));
        model.Filter = "";
        Assert.Equal(CommandCatalog.All.Count, model.Rows.Count);
    }

    [AvaloniaFact]
    public void F1OpensTheKeysWindow()
    {
        using var host = ShellHost.Open(Language.En);

        host.Window.Dispatch(CommandId.Keys);

        var commands = host.Provider.GetRequiredService<HelpCommands>();
        var window = commands.ShowKeys();
        Assert.NotNull(window);
        try
        {
            window.UpdateLayout();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "RowList");
            Assert.Equal(CommandCatalog.All.Count, list.ItemCount);
            Assert.Equal(host.Localizer.Get("cmd.keys"), window.Title);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AboutShowsTheVersionAndTheRepository()
    {
        var dialogs = new FakeDialogService();
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IDialogService>(dialogs));

        host.Window.Dispatch(CommandId.About);

        var shown = await dialogs.WaitShownAsync();
        Assert.Equal(host.Localizer.Get("cmd.about"), shown.Title);
        Assert.Contains(PropsVersion(), shown.Text, StringComparison.Ordinal);
        Assert.Contains("https://github.com/neijix/D3SKCOMMANDER", shown.Text, StringComparison.Ordinal);
        Assert.Contains("MIT", shown.Text, StringComparison.Ordinal);
        Assert.Contains(".NET", shown.Text, StringComparison.Ordinal);
        Assert.Contains("Avalonia", shown.Text, StringComparison.Ordinal);
        dialogs.Answer(DialogResult.Picked(0));
    }

    // The expected version comes from the props file, not from the app under test.
    private static string PropsVersion()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props))
            {
                return System.Text.RegularExpressions.Regex.Match(File.ReadAllText(props), "<Version>([^<]+)</Version>").Groups[1].Value;
            }
        }

        throw new FileNotFoundException("Directory.Build.props not found above " + AppContext.BaseDirectory);
    }
}
