using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Shell;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

/// <summary>Every field of the old single-page window sits on exactly one page and still loads and saves through its control.</summary>
public class SettingsFieldPagesTests
{
    private static readonly (string Page, string[] Fields)[] Map =
    [
        (SettingsCategories.General, ["LanguageBox", "FontBox", "UpdatesBox"]),
        (SettingsCategories.Panels, ["HiddenBox", "AutoSizesBox", "PreviewStartBox"]),
        (SettingsCategories.FileOperations, ["TrashBox", "ConfirmBox", "TrashFallbackBox", "LogBox"]),
        (SettingsCategories.Programs, ["EditorBox", "TerminalBox"]),
        (SettingsCategories.Columns, ["ColumnSetsSection"]),
        (SettingsCategories.Favorites, ["HotlistSection"]),
        (SettingsCategories.FavoriteTabs, ["FavoritesSection"]),
        (SettingsCategories.FileAssociations, ["AssociationsSection"]),
        (SettingsCategories.Toolbar, ["ToolbarSection"]),
        (SettingsCategories.UserCommands, ["CommandList", "AddButton", "EditButton", "RemoveButton"]),
        (SettingsCategories.QuickFindIndex, ["IndexRootsBox", "IndexExcludesBox", "IndexHoursBox"]),
        (SettingsCategories.SettingsFolder, ["FolderLabel", "FolderButton"]),
    ];

    [AvaloniaFact]
    public void EveryFieldIsOnExactlyOnePageTheOneTheDesignListsIt()
    {
        using var app = new Shell();
        var window = app.OpenSettings();
        var pageNames = SettingsCategories.All.Select(c => SettingsView.PageName(c.Id)).ToHashSet();
        Assert.Equal(SettingsCategories.All.Select(c => c.Id).Order(), Map.Select(m => m.Page).Order());

        foreach (var (page, fields) in Map)
        {
            foreach (var field in fields)
            {
                var matches = window.GetVisualDescendants().OfType<Control>().Where(c => c.Name == field).ToList();
                var control = Assert.Single(matches);
                var owners = control.GetVisualAncestors().OfType<Control>().Where(a => a.Name is not null && pageNames.Contains(a.Name)).Select(a => a.Name).ToList();
                Assert.Equal([SettingsView.PageName(page)], owners);
            }
        }
    }

    [AvaloniaFact]
    public void EveryFieldLoadsTheStoredValueAndSavesTheEditedOne()
    {
        using var app = new Shell();
        var stored = PreferencesCopy.Of(app.Settings.Preferences.Current);
        stored.Language = Language.Fr;
        stored.FontSize = 18;
        stored.CheckUpdates = false;
        stored.ShowHidden = true;
        stored.AutoFolderSizes = true;
        stored.PreviewStartSeconds = 30;
        stored.UseTrash = false;
        stored.ConfirmDelete = false;
        stored.ConfirmTrashFallback = false;
        stored.LogOperations = false;
        stored.Editor = "kate";
        stored.Terminal = "xterm";
        stored.IndexRoots = "/data";
        stored.IndexExcludes = "*/tmp";
        stored.IndexRefreshHours = 6;
        app.Settings.Preferences.Save(stored);

        var window = app.OpenSettings();

        Assert.Equal(1, Find<ComboBox>(window, "LanguageBox").SelectedIndex);
        Assert.Equal(18m, Find<NumericUpDown>(window, "FontBox").Value);
        Assert.False(Find<CheckBox>(window, "UpdatesBox").IsChecked);
        Assert.True(Find<CheckBox>(window, "HiddenBox").IsChecked);
        Assert.True(Find<CheckBox>(window, "AutoSizesBox").IsChecked);
        Assert.Equal(30m, Find<NumericUpDown>(window, "PreviewStartBox").Value);
        Assert.False(Find<CheckBox>(window, "TrashBox").IsChecked);
        Assert.False(Find<CheckBox>(window, "ConfirmBox").IsChecked);
        Assert.False(Find<CheckBox>(window, "TrashFallbackBox").IsChecked);
        Assert.False(Find<CheckBox>(window, "LogBox").IsChecked);
        Assert.Equal("kate", Find<TextBox>(window, "EditorBox").Text);
        Assert.Equal("xterm", Find<TextBox>(window, "TerminalBox").Text);
        Assert.Equal("/data", Find<TextBox>(window, "IndexRootsBox").Text);
        Assert.Equal("*/tmp", Find<TextBox>(window, "IndexExcludesBox").Text);
        Assert.Equal(6m, Find<NumericUpDown>(window, "IndexHoursBox").Value);

        Find<ComboBox>(window, "LanguageBox").SelectedIndex = 0;
        Find<NumericUpDown>(window, "FontBox").Value = 22;
        Find<CheckBox>(window, "UpdatesBox").IsChecked = true;
        Find<CheckBox>(window, "HiddenBox").IsChecked = false;
        Find<CheckBox>(window, "AutoSizesBox").IsChecked = false;
        Find<NumericUpDown>(window, "PreviewStartBox").Value = 45;
        Find<CheckBox>(window, "TrashBox").IsChecked = true;
        Find<CheckBox>(window, "ConfirmBox").IsChecked = true;
        Find<CheckBox>(window, "TrashFallbackBox").IsChecked = true;
        Find<CheckBox>(window, "LogBox").IsChecked = true;
        Find<TextBox>(window, "EditorBox").Text = "code";
        Find<TextBox>(window, "TerminalBox").Text = "wezterm";
        Find<TextBox>(window, "IndexRootsBox").Text = "/home";
        Find<TextBox>(window, "IndexExcludesBox").Text = "*/cache";
        Find<NumericUpDown>(window, "IndexHoursBox").Value = 12;
        Assert.IsType<SettingsViewModel>(window.View).Apply();

        window.Close();
        var saved = app.OpenSettings();
        Assert.NotSame(window, saved);
        Assert.Equal(0, Find<ComboBox>(saved, "LanguageBox").SelectedIndex);
        Assert.Equal(22m, Find<NumericUpDown>(saved, "FontBox").Value);
        Assert.True(Find<CheckBox>(saved, "UpdatesBox").IsChecked);
        Assert.False(Find<CheckBox>(saved, "HiddenBox").IsChecked);
        Assert.False(Find<CheckBox>(saved, "AutoSizesBox").IsChecked);
        Assert.Equal(45m, Find<NumericUpDown>(saved, "PreviewStartBox").Value);
        Assert.True(Find<CheckBox>(saved, "TrashBox").IsChecked);
        Assert.True(Find<CheckBox>(saved, "ConfirmBox").IsChecked);
        Assert.True(Find<CheckBox>(saved, "TrashFallbackBox").IsChecked);
        Assert.True(Find<CheckBox>(saved, "LogBox").IsChecked);
        Assert.Equal("code", Find<TextBox>(saved, "EditorBox").Text);
        Assert.Equal("wezterm", Find<TextBox>(saved, "TerminalBox").Text);
        Assert.Equal("/home", Find<TextBox>(saved, "IndexRootsBox").Text);
        Assert.Equal("*/cache", Find<TextBox>(saved, "IndexExcludesBox").Text);
        Assert.Equal(12m, Find<NumericUpDown>(saved, "IndexHoursBox").Value);
    }

    private static T Find<T>(ToolWindow window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private sealed class Shell : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly MainWindow _main;

        public Shell()
        {
            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            _provider = TestComposition.Build(text);
            _main = _provider.GetRequiredService<MainWindow>();
            _main.Show();
            Settings = _provider.GetRequiredService<D3skCommander.App.Startup.AppSettings>();
        }

        public D3skCommander.App.Startup.AppSettings Settings { get; }

        public ToolWindow OpenSettings()
        {
            Assert.True(_provider.GetRequiredService<IShellContributions>().Dispatch(CommandId.Settings));
            return _provider.GetRequiredService<IToolWindowHost>().Open(ToolKind.Settings);
        }

        public void Dispose()
        {
            _main.Close();
            _provider.Dispose();
        }
    }
}
