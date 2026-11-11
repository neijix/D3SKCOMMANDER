using Avalonia.Headless.XUnit;
using D3skCommander.App.Features.Settings;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Launch;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

/// <summary>The settings view model's draft: Apply enabled only while it differs from the live store; OK, Cancel, categories.</summary>
public sealed class SettingsDraftTests
{
    private static string Snapshot(SettingsHarness harness) =>
        string.Join("|", Directory.GetFiles(harness.Folder).Order().Select(f => Path.GetFileName(f) + ":" + Convert.ToHexString(File.ReadAllBytes(f))));

    private static SettingsViewModel Clean(SettingsHarness harness)
    {
        var model = harness.NewView();
        Assert.False(model.IsDirty);
        Assert.False(model.ApplyCommand.CanExecute(null));
        return model;
    }

    private static void AssertDirtyThenClean(SettingsViewModel model, Action change, Action revert)
    {
        change();
        Assert.True(model.IsDirty);
        Assert.True(model.ApplyCommand.CanExecute(null));
        revert();
        Assert.False(model.IsDirty);
        Assert.False(model.ApplyCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void TheCategoriesAreCoreSOrderWithLocalizedNames()
    {
        foreach (var language in new[] { Language.En, Language.Fr })
        {
            using var harness = new SettingsHarness(language: language);
            var model = harness.NewView();
            Assert.Equal(SettingsCategories.All.Select(c => c.Id), model.Categories.Select(c => c.Id));
            Assert.Equal(SettingsCategories.All.Select(c => harness.Text.Get(c.NameKey)), model.Categories.Select(c => c.Name));
            Assert.Equal(SettingsCategories.General, model.SelectedCategory.Id);
        }
    }

    [AvaloniaFact]
    public void SelectingACategoryKeepsTheDraftAndAnUnknownIdChangesNothing()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        model.Editor = "kate";
        model.Select(SettingsCategories.Toolbar);
        Assert.Equal(SettingsCategories.Toolbar, model.SelectedCategory.Id);
        Assert.Equal("kate", model.Editor);
        Assert.True(model.IsDirty);
        model.Select("nope");
        Assert.Equal(SettingsCategories.Toolbar, model.SelectedCategory.Id);
    }

    [AvaloniaFact]
    public void ThePanelsAndFormFieldsEachEnableApplyAndRevertingDisablesIt()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        var stored = harness.Settings.Preferences.Current;

        AssertDirtyThenClean(model, () => model.LanguageIndex = 1 - model.LanguageIndex, () => model.LanguageIndex = 1 - model.LanguageIndex);
        AssertDirtyThenClean(model, () => model.FontSize = 31, () => model.FontSize = (decimal)stored.FontSize);
        AssertDirtyThenClean(model, () => model.CheckUpdates = !model.CheckUpdates, () => model.CheckUpdates = !model.CheckUpdates);
        AssertDirtyThenClean(model, () => model.ShowHidden = !model.ShowHidden, () => model.ShowHidden = !model.ShowHidden);
        AssertDirtyThenClean(model, () => model.AutoFolderSizes = !model.AutoFolderSizes, () => model.AutoFolderSizes = !model.AutoFolderSizes);
        AssertDirtyThenClean(model, () => model.PreviewStartSeconds = 7, () => model.PreviewStartSeconds = stored.PreviewStartSeconds);
        AssertDirtyThenClean(model, () => model.UseTrash = !model.UseTrash, () => model.UseTrash = !model.UseTrash);
        AssertDirtyThenClean(model, () => model.ConfirmDelete = !model.ConfirmDelete, () => model.ConfirmDelete = !model.ConfirmDelete);
        AssertDirtyThenClean(model, () => model.ConfirmTrashFallback = !model.ConfirmTrashFallback, () => model.ConfirmTrashFallback = !model.ConfirmTrashFallback);
        AssertDirtyThenClean(model, () => model.LogOperations = !model.LogOperations, () => model.LogOperations = !model.LogOperations);
        AssertDirtyThenClean(model, () => model.Editor = "kate", () => model.Editor = stored.Editor);
        AssertDirtyThenClean(model, () => model.Terminal = "xterm", () => model.Terminal = stored.Terminal);
        AssertDirtyThenClean(model, () => model.IndexRoots = "/data", () => model.IndexRoots = stored.IndexRoots);
        AssertDirtyThenClean(model, () => model.IndexExcludes = "tmp", () => model.IndexExcludes = stored.IndexExcludes);
        AssertDirtyThenClean(model, () => model.IndexRefreshHours = 99, () => model.IndexRefreshHours = stored.IndexRefreshHours);
    }

    [AvaloniaFact]
    public void TheComparisonUsesWhatApplyWouldSave()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        model.Editor = "  " + harness.Settings.Preferences.Current.Editor + "  ";
        model.IndexRoots = harness.Settings.Preferences.Current.IndexRoots + " ";
        Assert.False(model.IsDirty);

        model.FontSize = 500;
        Assert.True(model.IsDirty);
        model.FontSize = (decimal)harness.Settings.Preferences.Current.FontSize;
        Assert.False(model.IsDirty);

        // An emptied number box saves its default, so it differs only when the store holds another value.
        model.PreviewStartSeconds = null;
        Assert.Equal(harness.Settings.Preferences.Current.PreviewStartSeconds != 0, model.IsDirty);
    }

    [AvaloniaFact]
    public void UserCommandsEnableApplyOnAddEditAndRemove()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        AssertDirtyThenClean(
            model,
            () => model.Commands.Add(new UserCommandRow(new UserCommand("Open", "xdg-open %s", false))),
            () => model.Commands.Clear());

        harness.Settings.Preferences.Save(WithCommand(harness));
        var loaded = Clean(harness);
        AssertDirtyThenClean(
            loaded,
            () => loaded.Commands[0] = new UserCommandRow(new UserCommand("Open", "other", false)),
            () => loaded.Commands[0] = new UserCommandRow(new UserCommand("Open", "xdg-open %s", false)));
        AssertDirtyThenClean(loaded, () => loaded.Commands.RemoveAt(0), () => loaded.Commands.Add(new UserCommandRow(new UserCommand("Open", "xdg-open %s", false))));
    }

    private static Preferences WithCommand(SettingsHarness harness)
    {
        var stored = PreferencesCopy.Of(harness.Settings.Preferences.Current);
        stored.UserCommands = [new UserCommand("Open", "xdg-open %s", false)];
        return stored;
    }

    [AvaloniaFact]
    public void TheColumnSetsEditorEnablesApply()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        AssertDirtyThenClean(model, () => model.ColumnSets.Add("Mine"), () => model.ColumnSets.DeleteSet());
    }

    [AvaloniaFact]
    public void TheToolbarEditorEnablesApply()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        AssertDirtyThenClean(model, () => model.Toolbar.AddSeparator(), () => model.Toolbar.Reset());
    }

    [AvaloniaFact]
    public void TheAssociationsEditorEnablesApply()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        var row = model.Associations.AddRule("n", "*.md");
        Assert.True(model.IsDirty);
        row.Open.Program = "p";
        Assert.True(model.IsDirty);
        model.Associations.Remove();
        Assert.False(model.IsDirty);

        // A rule without masks is dropped by Apply, so it does not count.
        var empty = model.Associations.AddRule("n", "");
        empty.Open.Program = "p";
        Assert.False(model.IsDirty);
    }

    [AvaloniaFact]
    public void TheFavoriteTabsEditorEnablesApplyForTheModeAndForEdits()
    {
        using var harness = new SettingsHarness();
        harness.Settings.FavoriteTabs.Save(Favorites("A", "B"));
        var model = Clean(harness);
        AssertDirtyThenClean(model, () => model.Favorites.ModeIndex = 1 - model.Favorites.ModeIndex, () => model.Favorites.ModeIndex = 1 - model.Favorites.ModeIndex);

        model.Favorites.Rename("A", "Z");
        Assert.True(model.IsDirty);
        model.Favorites.Rename("Z", "A");
        Assert.False(model.IsDirty);

        model.Favorites.SelectedRow = model.Favorites.Rows[0];
        model.Favorites.Delete();
        Assert.True(model.IsDirty);
    }

    private static FavoriteTabs Favorites(params string[] names)
    {
        var tabs = new FavoriteTabs();
        foreach (var name in names)
        {
            tabs.Save(new FavoriteTabSet { Name = name });
        }

        return tabs;
    }

    [AvaloniaFact]
    public void ApplySavesAndDisablesApplyAgain()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        model.Editor = "kate";
        model.Toolbar.AddSeparator();
        Assert.True(model.IsDirty);

        model.ApplyCommand.Execute(null);

        Assert.Equal("kate", harness.Settings.Preferences.Current.Editor);
        Assert.False(model.IsDirty);
        Assert.False(model.ApplyCommand.CanExecute(null));
        Assert.Equal(harness.Text.Get("settings.saved"), model.Note);
    }

    [AvaloniaFact]
    public void ALanguageApplyLeavesApplyDisabledToo()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        model.LanguageIndex = 1;
        model.Apply();
        Assert.False(model.IsDirty);
    }

    [AvaloniaFact]
    public void AnOutsideChangeIsSeenOnRequestAndIsTheBaselineAfterwards()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        var outside = PreferencesCopy.Of(harness.Settings.Preferences.Current);
        outside.Editor = "vim";
        harness.Settings.Preferences.Save(outside);

        Assert.False(model.IsDirty);
        model.RefreshDraftState();
        Assert.True(model.IsDirty);

        model.Editor = "vim";
        Assert.False(model.IsDirty);
    }

    [AvaloniaFact]
    public void OkWithAnUnchangedDraftOnlyCloses()
    {
        using var harness = new SettingsHarness();
        harness.Settings.Preferences.Save(PreferencesCopy.Of(harness.Settings.Preferences.Current));
        var before = Snapshot(harness);
        var model = Clean(harness);
        var closed = 0;
        model.CloseRequested += () => closed++;

        model.OkCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal("", model.Note);
        Assert.Equal(before, Snapshot(harness));
    }

    [AvaloniaFact]
    public void OkWithAChangedDraftAppliesThenCloses()
    {
        using var harness = new SettingsHarness();
        var model = Clean(harness);
        var closed = 0;
        model.CloseRequested += () => closed++;
        model.Terminal = "xterm";

        model.OkCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal("xterm", harness.Settings.Preferences.Current.Terminal);
        Assert.Equal(harness.Text.Get("settings.saved"), model.Note);
    }

    [AvaloniaFact]
    public void CancelClosesAndSavesNothing()
    {
        using var harness = new SettingsHarness();
        harness.Settings.Preferences.Save(PreferencesCopy.Of(harness.Settings.Preferences.Current));
        var before = Snapshot(harness);
        var model = Clean(harness);
        var closed = 0;
        model.CloseRequested += () => closed++;
        model.Editor = "kate";
        model.Toolbar.AddSeparator();
        model.Favorites.ModeIndex = 1 - model.Favorites.ModeIndex;

        model.CancelCommand.Execute(null);

        Assert.Equal(1, closed);
        Assert.Equal(before, Snapshot(harness));
        Assert.Equal("", harness.Settings.Preferences.Current.Editor);
        Assert.Equal("", harness.NewView().Editor);
    }

    [AvaloniaFact]
    public void TheLabelsCarryCancelInsteadOfClose()
    {
        using var harness = new SettingsHarness();
        Assert.Equal(harness.Text.Get("cancel"), harness.NewView().Labels.Cancel);
    }
}
