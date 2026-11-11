using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Settings;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Associations;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Panels;
using D3skCommander.Core.Toolbar;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

/// <summary>Each list editor model tells its owner when its draft changed (and only then).</summary>
public sealed class SettingsEditorChangeTests
{
    private static Localizer Text() => Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    private static ColumnSet[] Sets() =>
    [
        new ColumnSet("Default", [new ColumnSetColumn(ColumnId.Size)]),
        new ColumnSet("Details", [new ColumnSetColumn(ColumnId.Size), new ColumnSetColumn(ColumnId.Modified)]),
    ];


    [AvaloniaFact]
    public void TheToolbarEditorNotifiesOnEveryDraftChange()
    {
        var model = new ToolbarEditorModel(Text(), [ToolbarItem.ForCommand(CommandId.Refresh)], ["tool"]);
        var count = 0;
        model.DraftChanged += (_, _) => count++;
        Assert.Equal(0, count);

        var row = model.AddSeparator();
        Assert.True(count > 0);
        count = 0;
        row = model.AddProgram("a");
        Assert.True(count > 0);
        count = 0;

        row.Program = "b";
        Assert.True(count > 0);
        count = 0;
        row.Tooltip = "tip";
        Assert.True(count > 0);
        count = 0;

        model.SelectedRow = row;
        model.MoveUp();
        Assert.True(count > 0);
        count = 0;

        model.Remove();
        Assert.True(count > 0);
        count = 0;

        model.Reset();
        Assert.True(count > 0);
        count = 0;

        // A row edit after a reset still reaches the owner; a plain selection does not.
        model.SelectedRow = model.Rows[0];
        Assert.Equal(0, count);
        model.Rows[0].Tooltip = "x";
        Assert.True(count > 0);
        count = 0;
    }

    [AvaloniaFact]
    public void TheToolbarEditorStaysQuietWhileItLoads()
    {
        var count = 0;
        var model = new ToolbarEditorModel(Text(), ToolbarDefaults.Items(), []);
        model.DraftChanged += (_, _) => count++;
        model.SelectedRow = model.Rows[1];
        Assert.Equal(0, count);
    }

    [AvaloniaFact]
    public void TheAssociationsEditorNotifiesOnRuleExtraAndActionEdits()
    {
        var rule = new AssociationRule("r", "*.txt", new AssociationAction("ed"), extras: [new AssociationAction("x", "", "", "Extra")]);
        var model = new AssociationsEditorModel(Text(), [rule]);
        var count = 0;
        model.DraftChanged += (_, _) => count++;
        Assert.Equal(0, count);

        var row = model.AddRule("n", "*.md");
        Assert.True(count > 0);
        count = 0;
        row.Masks = "*.markdown";
        Assert.True(count > 0);
        count = 0;
        row.Open.Program = "p";
        Assert.True(count > 0);
        count = 0;
        row.AddExtra();
        Assert.True(count > 0);
        count = 0;
        row.Extras[0].Program = "q";
        Assert.True(count > 0);
        count = 0;
        row.RemoveExtra();
        Assert.True(count > 0);
        count = 0;

        model.SelectedRow = model.Rows[0];
        Assert.Equal(0, count);
        model.Rows[0].Extras[0].Name = "Renamed";
        Assert.True(count > 0);
        count = 0;
        model.Rows[0].Edit.Arguments = "a";
        Assert.True(count > 0);
        count = 0;

        model.MoveDown();
        Assert.True(count > 0);
        count = 0;
        // A move lists the same row as removed and added; a later field edit must still notify.
        Assert.NotNull(model.SelectedRow);
        model.SelectedRow.Masks = "*.moved";
        Assert.True(count > 0);
        count = 0;
        model.Remove();
        Assert.True(count > 0);
        count = 0;
    }

    [AvaloniaFact]
    public void TheColumnSetsEditorNotifiesOnSetAndColumnChangesButNotOnSelection()
    {
        var text = Text();
        var model = new ColumnSetsEditorModel(text, new FakeDialogService(), Sets());
        var count = 0;
        model.DraftChanged += (_, _) => count++;

        model.SelectedSet = model.Sets[1];
        Assert.Equal(0, count);

        var made = model.Add("mine");
        Assert.True(count > 0);
        count = 0;
        Assert.Same(made, model.SelectedSet);

        model.Rows.First(row => !row.IsShown).IsShown = true;
        Assert.True(count > 0);
        count = 0;
        model.Rows[0].Width = 77;
        Assert.True(count > 0);
        count = 0;

        model.SelectedRow = model.Rows[0];
        model.MoveDown();
        Assert.True(count > 0);
        count = 0;

        model.DeleteSet();
        Assert.True(count > 0);
        count = 0;
    }

    [AvaloniaFact]
    public async Task TheColumnSetsEditorNotifiesOnRename()
    {
        var dialogs = new FakeDialogService();
        var model = new ColumnSetsEditorModel(Text(), dialogs, Sets());
        model.Add("mine");
        var count = 0;
        model.DraftChanged += (_, _) => count++;

        var asking = model.RenameSetAsync();
        var request = await dialogs.WaitShownAsync();
        ((TextDialogField)request.Form!.Fields[0]).Text = "renamed";
        dialogs.Answer(DialogResult.Ok());
        await asking;

        Assert.Equal("renamed", model.SelectedSet?.Name);
        Assert.True(count > 0);
        count = 0;
    }

    [AvaloniaFact]
    public void TheFavoriteTabsEditorNotifiesOnRenameDeleteMoveAndModeChange()
    {
        var saved = new FavoriteTabs();
        foreach (var name in new[] { "a", "b", "c" })
        {
            saved.Save(new FavoriteTabSet { Name = name });
        }

        var model = new FavoriteTabsEditorModel(Text(), new FakeDialogService(), saved, FavoriteRestoreMode.Replace);
        var count = 0;
        model.DraftChanged += (_, _) => count++;
        Assert.Equal(0, count);

        model.SelectedRow = model.Rows[0];
        Assert.Equal(0, count);
        model.MoveDownCommand.Execute(null);
        Assert.True(count > 0);
        count = 0;
        Assert.False(model.Rename("zzz", "y"));
        Assert.Equal(0, count);
        Assert.True(model.Rename("c", "z"));
        Assert.True(count > 0);
        count = 0;
        model.SelectedRow = model.Rows[0];
        model.DeleteCommand.Execute(null);
        Assert.True(count > 0);
        count = 0;
        model.ModeIndex = 1;
        Assert.True(count > 0);
        count = 0;
    }
}
