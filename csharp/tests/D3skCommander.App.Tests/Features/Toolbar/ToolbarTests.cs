using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Launch;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Features.Toolbar;
using D3skCommander.App.Icons;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.Tests.Features.Launch;
using D3skCommander.App.Tests.Features.Settings;
using D3skCommander.App.Tests.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using D3skCommander.Core.Toolbar;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Toolbar;

public class ToolbarTests
{
    [AvaloniaFact]
    public void TheToolbarIsVisibleByDefaultAndShowToolbarHidesItAndSavesTheChoice()
    {
        using var host = ShellHost.Open(Language.En);
        var settings = host.Provider.GetRequiredService<AppSettings>();
        var shell = host.Provider.GetRequiredService<IShellContributions>();
        var bar = ToolbarOf(host);
        try
        {
            Assert.True(bar.IsVisible);
            Assert.True(settings.Preferences.Current.ShowToolbar);

            Assert.True(shell.Dispatch(CommandId.ShowToolbar));

            Assert.False(bar.IsVisible);
            Assert.False(settings.Preferences.Current.ShowToolbar);
        }
        finally
        {
            if (!settings.Preferences.Current.ShowToolbar)
            {
                shell.Dispatch(CommandId.ShowToolbar);
            }
        }

        Assert.True(bar.IsVisible);
        Assert.True(settings.Preferences.Current.ShowToolbar);
    }

    [AvaloniaFact]
    public void TheContextMenuOfTheToolbarCustomizesIt()
    {
        var opened = 0;
        using var host = ShellHost.Open(Language.En, services =>
            services.AddSingleton<IShellModule>(new Probe(CommandId.CustomizeToolbar, () => opened++)));
        var bar = ToolbarOf(host);

        var item = Assert.IsType<MenuItem>(Assert.Single(bar.ContextMenu!.Items));
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(1, opened);
        Assert.Equal(host.Localizer.Get("cmd.customize_toolbar"), item.Header);
    }

    [AvaloniaFact]
    public void ClickingACommandButtonRunsThatCommandAndTheTooltipNamesItWithItsKey()
    {
        var copies = 0;
        using var host = ShellHost.Open(Language.En, services =>
            services.AddSingleton<IShellModule>(new Probe(CommandId.Copy, () => copies++)));
        var button = ButtonFor(host, item => item.Command == CommandId.Copy);

        Assert.Equal("Copy (F5)", ToolTip.GetTip(button));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, copies);
    }

    [AvaloniaFact]
    public void AProgramButtonStartsTheProgramWithTheExpandedArguments()
    {
        var runner = new RecordingRunner();
        using var host = ShellHost.Open(Language.En, services => services.AddSingleton<IProcessRunner>(runner));
        var settings = host.Provider.GetRequiredService<AppSettings>();
        var before = settings.Preferences.Current.Toolbar.ToList();
        try
        {
            SaveToolbar(settings, [ToolbarItem.ForProgram("/opt/my tool/run", "--flag %s", "/tmp", "program", "Run my tool")]);

            var button = ButtonFor(host, item => item.Kind == ToolbarItemKind.Program);
            Assert.Equal("Run my tool", ToolTip.GetTip(button));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var start = Assert.IsType<ProcessRequest.Argv>(Assert.Single(runner.Started));
            Assert.Equal("/opt/my tool/run", start.Program);
            Assert.Equal("--flag", Assert.Single(start.Arguments));
            Assert.Equal("/tmp", start.WorkingDirectory);
        }
        finally
        {
            SaveToolbar(settings, before);
        }
    }

    [AvaloniaFact]
    public void AButtonIsDisabledWhenNobodyCanRunItsCommand()
    {
        using var host = ShellHost.Open(Language.En);
        var shell = host.Provider.GetRequiredService<IShellContributions>();
        var model = host.Provider.GetRequiredService<ToolbarModel>();

        foreach (var button in model.Buttons.Where(button => button.Item.Command is not null))
        {
            Assert.Equal(shell.CanExecute(button.Item.Command!.Value), button.IsEnabled);
        }
    }

    [AvaloniaFact]
    public void TheSettingsEditorAddsAButtonMovesItAndSavingChangesTheToolbar()
    {
        using var harness = new SettingsHarness();
        var model = harness.NewView();
        var editor = model.Toolbar;
        Assert.Equal(ToolbarDefaults.Items().Count, editor.Rows.Count);

        var added = editor.AddCommand(CommandId.Hotlist);
        Assert.Same(added, editor.Rows[1]);
        editor.MoveUp();
        Assert.Same(added, editor.Rows[0]);
        editor.MoveDown();
        editor.MoveDown();
        Assert.Same(added, editor.Rows[2]);
        added.Tooltip = "My stars";
        added.Icon = editor.Icons.First(choice => choice.Name == "star");
        model.Apply();

        var saved = harness.Settings.Preferences.Current.Toolbar;
        Assert.Equal(ToolbarDefaults.Items().Count + 1, saved.Count);
        Assert.Equal(ToolbarItem.ForCommand(CommandId.Hotlist, "star", "My stars"), saved[2]);
    }

    [AvaloniaFact]
    public void TheEditorCoversEveryKindRemovesAndResets()
    {
        using var harness = new SettingsHarness();
        var editor = harness.NewView().Toolbar;
        Assert.Equal(ToolbarDefaults.Items(), editor.Result());

        var program = editor.AddProgram("/opt/my tool/run", "-x %s", "/work");
        editor.AddSeparator();
        Assert.Equal(ToolbarIcons.Program, program.ToItem().EffectiveIcon());
        Assert.True(editor.SelectedRow!.IsSeparator);
        Assert.Contains(editor.Result(), item => item.Kind == ToolbarItemKind.Program && item.WorkingFolder == "/work");

        var count = editor.Rows.Count;
        editor.Remove();
        Assert.Equal(count - 1, editor.Rows.Count);
        Assert.Contains(program, editor.Rows);

        editor.Reset();
        Assert.Equal(ToolbarDefaults.Items(), editor.Result());
    }

    [Fact]
    public void EveryIconNameHasADrawingAndNoDrawingIsLeftOver()
    {
        Assert.Equal(ToolbarIcons.Names.Order(StringComparer.Ordinal), ToolbarGlyphs.Names.Order(StringComparer.Ordinal));
    }

    private static void SaveToolbar(AppSettings settings, IReadOnlyList<ToolbarItem> items)
    {
        var preferences = PreferencesCopy.Of(settings.Preferences.Current);
        preferences.Toolbar = [.. items];
        settings.Preferences.Save(preferences);
    }

    private static ToolbarView ToolbarOf(ShellHost host) =>
        host.Window.GetVisualDescendants().OfType<ToolbarView>().Single();

    private static Button ButtonFor(ShellHost host, Func<ToolbarItem, bool> match) =>
        ToolbarOf(host).GetVisualDescendants().OfType<Button>().First(button => button.Tag is ToolbarItem item && match(item));

    private sealed class Probe(CommandId command, Action run) : IShellModule
    {
        public void Contribute(IShellContributions shell) => shell.Handle(command, run);
    }
}
