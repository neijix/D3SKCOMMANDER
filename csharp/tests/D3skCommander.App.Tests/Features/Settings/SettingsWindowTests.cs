using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

public class SettingsWindowTests
{
    [AvaloniaFact]
    public void F9OpensTheSettingsToolWindowWithTheStoredValues()
    {
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        using var provider = TestComposition.Build(text);
        var main = provider.GetRequiredService<MainWindow>();
        main.Show();
        var tools = provider.GetRequiredService<IToolWindowHost>();
        var shell = provider.GetRequiredService<IShellContributions>();
        Assert.True(shell.CanExecute(CommandId.Settings));
        Assert.True(shell.CanExecute(CommandId.SettingsFolder));
        Assert.False(tools.IsOpen(ToolKind.Settings));

        Assert.True(shell.Dispatch(CommandId.Settings));

        Assert.True(tools.IsOpen(ToolKind.Settings));
        var window = tools.Open(ToolKind.Settings);
        var model = Assert.IsType<SettingsViewModel>(window.View);
        Assert.Equal(provider.GetRequiredService<D3skCommander.App.Startup.AppSettings>().Preferences.Current.ShowHidden, model.ShowHidden);
        Assert.NotNull(Find(window, "LanguageBox"));
        window.Close();
        main.Close();
    }

    [AvaloniaFact]
    public void TheWindowShowsEveryPreferenceOfTheStore()
    {
        using var harness = new SettingsHarness();
        var stored = PreferencesCopy.Of(harness.Settings.Preferences.Current);
        stored.Language = Language.Fr;
        stored.FontSize = 18;
        stored.ShowHidden = true;
        stored.Editor = "kate";
        stored.Terminal = "xterm";
        stored.Columns = new Columns(false, true, false, true);
        stored.UserCommands = [new UserCommand("Open", "xdg-open %s", true)];
        harness.Settings.Preferences.Save(stored);

        var model = harness.NewView();
        var window = new Window { Width = 560, Height = 640, Content = model.CreateContent() };
        window.Show();

        Assert.Equal(1, model.LanguageIndex);
        Assert.Equal(18m, model.FontSize);
        Assert.True(Find<CheckBox>(window, "HiddenBox").IsChecked);
        Assert.Equal("kate", Find<TextBox>(window, "EditorBox").Text);
        Assert.Equal("xterm", Find<TextBox>(window, "TerminalBox").Text);
        Assert.False(Find<CheckBox>(window, "ExtensionBox").IsChecked);
        Assert.True(Find<CheckBox>(window, "SizeBox").IsChecked);
        Assert.Equal("Open — xdg-open %s", Assert.Single(model.Commands).Display);
        Assert.Contains("current", Find<TextBlock>(window, "FolderLabel").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void ApplySavesAndKeepsTheFieldsTheWindowDoesNotEdit()
    {
        using var harness = new SettingsHarness();
        var stored = PreferencesCopy.Of(harness.Settings.Preferences.Current);
        stored.DarkMode = false;
        harness.Settings.Preferences.Save(stored);
        var model = harness.NewView();

        model.ShowHidden = true;
        model.UseTrash = false;
        model.Editor = " code ";
        model.FontSize = 20;
        model.Apply();

        var now = harness.Settings.Preferences.Current;
        Assert.True(now.ShowHidden);
        Assert.False(now.UseTrash);
        Assert.Equal("code", now.Editor);
        Assert.Equal(20f, now.FontSize);
        Assert.False(now.DarkMode);
        Assert.Equal(harness.Text.Get("settings.saved"), model.Note);

        harness.Settings.Flush();
        using var reread = new D3skCommander.Core.Settings.PreferencesStore(harness.Folder);
        Assert.True(reread.Current.ShowHidden);
        Assert.False(reread.Current.DarkMode);
    }

    [AvaloniaFact]
    public void TheLogToggleIsLive()
    {
        using var harness = new SettingsHarness();
        var model = harness.NewView();
        Assert.False(harness.Log.Enabled);

        model.LogOperations = true;
        model.Apply();
        Assert.True(harness.Log.Enabled);

        model.LogOperations = false;
        model.Apply();
        Assert.False(harness.Log.Enabled);
    }

    [AvaloniaFact]
    public void ALanguageChangeSwitchesTheLocalizerAndSaysWhatNeedsARestart()
    {
        using var harness = new SettingsHarness(language: Language.En);
        var model = harness.NewView();

        model.LanguageIndex = 1;
        model.Apply();

        Assert.Equal(Language.Fr, harness.Text.Language);
        Assert.Equal(Language.Fr, harness.Settings.Preferences.Current.Language);
        Assert.Equal(harness.Text.Get("settings.restart_language"), model.Note);
        Assert.Equal("La langue des menus s'applique au prochain démarrage.", model.Note);
    }

    [AvaloniaFact]
    public async Task UserCommandsCanBeAddedEditedAndRemoved()
    {
        using var harness = new SettingsHarness();
        var model = harness.NewView();
        var stop = TestContext.Current.CancellationToken;

        var adding = model.AddCommandAsync();
        var add = await harness.Dialogs.WaitShownAsync();
        var fields = add.Form!.Fields;
        ((TextDialogField)fields[0]).Text = "List";
        Assert.False(add.Form.CanAccept);
        ((TextDialogField)fields[1]).Text = "ls %s";
        Assert.True(add.Form.CanAccept);
        harness.Dialogs.Answer(DialogResult.Ok());
        await adding.WaitAsync(TimeSpan.FromSeconds(10), stop);
        Assert.Equal("List — ls %s", Assert.Single(model.Commands).Display);

        var editing = model.EditCommandAsync();
        var edit = await harness.Dialogs.WaitShownAsync();
        Assert.Equal("List", ((TextDialogField)edit.Form!.Fields[0]).Text);
        ((TextDialogField)edit.Form.Fields[1]).Text = "ls -l %s";
        ((CheckDialogField)edit.Form.Fields[2]).IsChecked = true;
        harness.Dialogs.Answer(DialogResult.Ok());
        await editing.WaitAsync(TimeSpan.FromSeconds(10), stop);
        var command = Assert.Single(model.Commands).Command;
        Assert.Equal("ls -l %s", command.Command);
        Assert.True(command.OncePerFile);

        model.Apply();
        Assert.Single(harness.Settings.Preferences.Current.UserCommands);

        model.RemoveCommand();
        Assert.Empty(model.Commands);
    }

    private static Control Find(Window window, string name) => Find<Control>(window, name);

    private static Control Find(ToolWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}
