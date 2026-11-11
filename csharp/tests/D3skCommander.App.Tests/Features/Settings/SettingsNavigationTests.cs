using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

/// <summary>The settings window's sidebar, pages, deep links, Cancel / close / Esc and activation, through the real tool window host.</summary>
public class SettingsNavigationTests
{
    private static readonly string[] ListEditorIds =
    [
        SettingsCategories.Columns,
        SettingsCategories.FavoriteTabs,
        SettingsCategories.FileAssociations,
        SettingsCategories.Toolbar,
    ];

    [AvaloniaTheory]
    [InlineData(Language.En)]
    [InlineData(Language.Fr)]
    public void TheSidebarListsTheElevenCategoriesInOrderWithLocalizedNames(Language language)
    {
        var text = Localizer.Load(settingsLanguage: language, systemLanguage: language);
        using var app = new App(text);
        var window = app.OpenSettings();

        var list = Find<ListBox>(window, "CategoryList");
        var names = list.Items.OfType<SettingsCategoryItem>().ToList();

        Assert.Equal(11, names.Count);
        Assert.Equal(SettingsCategories.All.Select(c => c.Id), names.Select(n => n.Id));
        Assert.Equal(SettingsCategories.All.Select(c => text.Get(c.NameKey)), names.Select(n => n.Name));
        Assert.Equal(SettingsCategories.General, ((SettingsCategoryItem)list.SelectedItem!).Id);
    }

    [AvaloniaFact]
    public void SelectingACategoryShowsOnlyItsPageAndItsTitle()
    {
        using var app = new App();
        var window = app.OpenSettings();
        var model = Model(window);

        foreach (var category in SettingsCategories.All)
        {
            model.Select(category.Id);

            var visible = SettingsCategories.All.Where(c => Page(window, c.Id).IsVisible).Select(c => c.Id);
            Assert.Equal([category.Id], visible);
            Assert.Equal(model.Categories.First(c => c.Id == category.Id).Name, Find<TextBlock>(window, "PageTitle").Text);
        }
    }

    [AvaloniaFact]
    public void ClickingACategoryInTheSidebarSelectsIt()
    {
        using var app = new App();
        var window = app.OpenSettings();
        var list = Find<ListBox>(window, "CategoryList");

        list.SelectedIndex = 7;

        Assert.Equal(SettingsCategories.Toolbar, Model(window).SelectedCategory.Id);
        Assert.True(Page(window, SettingsCategories.Toolbar).IsVisible);
    }

    [AvaloniaFact]
    public void EveryPageStaysInTheTreeAndFormPagesScrollWhileListPagesDoNot()
    {
        using var app = new App();
        var window = app.OpenSettings();

        foreach (var category in SettingsCategories.All)
        {
            var page = Page(window, category.Id);
            var isForm = !ListEditorIds.Contains(category.Id) && category.Id != SettingsCategories.UserCommands;
            Assert.Equal(isForm, page is ScrollViewer);
        }

        Assert.NotNull(Find<ListBox>(window, "CommandList"));
        Assert.NotNull(Find<Control>(window, "HiddenBox"));
    }

    [AvaloniaFact]
    public void ListEditorPagesShowOnlyThePageTitleAsHeading()
    {
        using var app = new App();
        var window = app.OpenSettings();
        var model = Model(window);

        foreach (var id in ListEditorIds)
        {
            model.Select(id);
            var name = model.SelectedCategory.Name;
            var headings = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.Text == name && !t.GetVisualAncestors().OfType<ListBox>().Any(l => l.Name == "CategoryList"))
                .ToList();

            var heading = Assert.Single(headings);
            Assert.Equal("PageTitle", heading.Name);
        }
    }

    [AvaloniaFact]
    public void F9OpensOnGeneralAtTheTopOfThePage()
    {
        using var app = new App();

        var window = app.OpenSettings();

        Assert.Equal(SettingsCategories.General, Model(window).SelectedCategory.Id);
        Assert.Equal(0, Assert.IsType<ScrollViewer>(Page(window, SettingsCategories.General)).Offset.Y);
        Assert.Equal(860, window.Width);
    }

    [AvaloniaFact]
    public void ShowingAFormPageAgainScrollsItToItsTop()
    {
        using var harness = new SettingsHarness();
        var model = harness.NewView();
        var window = new Window { Width = 700, Height = 150, Content = model.CreateContent() };
        window.Show();
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == SettingsView.PageName(SettingsCategories.QuickFindIndex));
        model.Select(SettingsCategories.QuickFindIndex);
        window.UpdateLayout();
        scroller.Offset = new Avalonia.Vector(0, 40);
        Assert.True(scroller.Offset.Y > 0);

        model.Select(SettingsCategories.General);
        model.Select(SettingsCategories.QuickFindIndex);

        Assert.Equal(0, scroller.Offset.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void F9OnAnOpenWindowKeepsItsPageAndAfterACloseReopensOnGeneral()
    {
        using var app = new App();
        var window = app.OpenSettings();
        Model(window).Select(SettingsCategories.Toolbar);

        Assert.True(app.Shell.Dispatch(CommandId.Settings));
        var same = app.Tools.Open(ToolKind.Settings);

        Assert.Same(window, same);
        Assert.Equal(SettingsCategories.Toolbar, Model(same).SelectedCategory.Id);

        same.Close();
        Assert.False(app.Tools.IsOpen(ToolKind.Settings));
        Assert.True(app.Shell.Dispatch(CommandId.Settings));
        var reopened = app.Tools.Open(ToolKind.Settings);

        Assert.NotSame(window, reopened);
        Assert.Equal(SettingsCategories.General, Model(reopened).SelectedCategory.Id);
        reopened.Close();
    }

    [AvaloniaTheory]
    [InlineData(CommandId.EditColumnSets, SettingsCategories.Columns)]
    [InlineData(CommandId.CustomizeToolbar, SettingsCategories.Toolbar)]
    [InlineData(CommandId.ManageFavoriteTabs, SettingsCategories.FavoriteTabs)]
    [InlineData(CommandId.EditFileAssociations, SettingsCategories.FileAssociations)]
    public void ADeepLinkOpensOrSwitchesTheWindowToItsPage(CommandId command, string category)
    {
        using var app = new App();

        Assert.True(app.Shell.Dispatch(command));
        var window = app.Tools.Open(ToolKind.Settings);
        Assert.Equal(category, Model(window).SelectedCategory.Id);
        Assert.True(Page(window, category).IsVisible);

        Model(window).Select(SettingsCategories.Programs);
        Assert.True(app.Shell.Dispatch(command));

        Assert.Same(window, app.Tools.Open(ToolKind.Settings));
        Assert.Equal(category, Model(window).SelectedCategory.Id);
        window.Close();
    }

    [AvaloniaFact]
    public void ADeepLinkOnAnOpenWindowKeepsTheUnappliedEditsAndSavesNothing()
    {
        using var app = new App();
        var window = app.OpenSettings();
        var model = Model(window);
        var before = app.StoreBytes();
        model.ShowHidden = !model.ShowHidden;

        Assert.True(app.Shell.Dispatch(CommandId.EditColumnSets));

        Assert.Equal(SettingsCategories.Columns, model.SelectedCategory.Id);
        Assert.NotEqual(app.Settings.Preferences.Current.ShowHidden, model.ShowHidden);
        Assert.True(model.IsDirty);
        Assert.Equal(before, app.StoreBytes());
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("button")]
    [InlineData("close")]
    [InlineData("escape")]
    public void CancelTheTitleBarCloseAndEscDiscardTheEdits(string how)
    {
        using var app = new App();
        var window = app.OpenSettings();
        var model = Model(window);
        var stored = app.Settings.Preferences.Current.ShowHidden;
        model.ShowHidden = !stored;
        model.FontSize = 30;
        var before = app.StoreBytes();

        switch (how)
        {
            case "button":
                var cancel = Find<Button>(window, "CancelButton");
                Assert.NotNull(cancel.Command);
                cancel.Command.Execute(cancel.CommandParameter);
                break;
            case "close":
                window.Close();
                break;
            default:
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                break;
        }

        Dispatcher.UIThread.RunJobs();
        Assert.False(app.Tools.IsOpen(ToolKind.Settings));
        app.Settings.Flush();
        Assert.Equal(before, app.StoreBytes());
        Assert.Equal(stored, app.Settings.Preferences.Current.ShowHidden);

        var reopened = app.OpenSettings();
        Assert.Equal(stored, Model(reopened).ShowHidden);
        Assert.Equal((decimal)app.Settings.Preferences.Current.FontSize, Model(reopened).FontSize);
        Assert.False(Model(reopened).IsDirty);
        reopened.Close();
    }

    [AvaloniaFact]
    public void ActivatingTheWindowReEvaluatesTheApplyButton()
    {
        using var app = new App();
        var window = app.OpenSettings();
        var model = Model(window);
        var apply = Find<Button>(window, "ApplyButton");
        var original = PreferencesCopy.Of(app.Settings.Preferences.Current);
        Assert.False(apply.IsEffectivelyEnabled);

        try
        {
            var outside = PreferencesCopy.Of(original);
            outside.ShowHidden = !original.ShowHidden;
            app.Settings.Preferences.Save(outside);
            Assert.False(model.IsDirty);

            window.Activate();
            Dispatcher.UIThread.RunJobs();

            Assert.True(model.IsDirty);
            Assert.True(apply.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
            app.Settings.Preferences.Save(original);
        }
    }

    private static SettingsViewModel Model(ToolWindow window) => Assert.IsType<SettingsViewModel>(window.View);

    private static Control Page(ToolWindow window, string categoryId) =>
        Find<Control>(window, SettingsView.PageName(categoryId));

    private static T Find<T>(ToolWindow window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    /// <summary>The application shell with its real tool window host; closes the main window when disposed.</summary>
    private sealed class App : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly MainWindow _main;

        public App(ILocalizer? text = null)
        {
            _provider = TestComposition.Build(text ?? Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En));
            _main = _provider.GetRequiredService<MainWindow>();
            _main.Show();
            Tools = _provider.GetRequiredService<IToolWindowHost>();
            Shell = _provider.GetRequiredService<IShellContributions>();
            Settings = _provider.GetRequiredService<AppSettings>();
        }

        public IToolWindowHost Tools { get; }

        public IShellContributions Shell { get; }

        public AppSettings Settings { get; }

        public ToolWindow OpenSettings()
        {
            Assert.True(Shell.Dispatch(CommandId.Settings));
            return Tools.Open(ToolKind.Settings);
        }

        /// <summary>The bytes of every preferences file in the settings folder, to prove a close wrote nothing.</summary>
        public string StoreBytes()
        {
            Settings.Flush();
            return string.Join(
                "|",
                Directory.GetFiles(Settings.Directory, "preferences*").Order(StringComparer.Ordinal)
                    .Select(f => f + ":" + Convert.ToBase64String(File.ReadAllBytes(f))));
        }

        public void Dispose()
        {
            _main.Close();
            _provider.Dispose();
        }
    }
}
