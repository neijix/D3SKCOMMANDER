using Avalonia.Headless.XUnit;
using D3skCommander.App.Dialogs;
using D3skCommander.Core.Settings;
using Xunit;

namespace D3skCommander.App.Tests.Features.Settings;

/// <summary>The Favorites page adds, renames, removes and reorders the hotlist; Apply saves it, Cancel leaves it.</summary>
public class HotlistEditorTests
{
    [AvaloniaFact]
    public async Task TheFavoritesPageRenamesRemovesAndMovesAndApplySavesTheList()
    {
        using var harness = new SettingsHarness();
        harness.Settings.Hotlist.Save(new Hotlist
        {
            Items = [new HotlistItem("Docs", "/home/docs"), new HotlistItem("Work", "/var/work"), new HotlistItem("Report", "/home/r.pdf")],
        });
        var model = harness.NewView();
        var page = model.Hotlist;
        Assert.Equal(["Docs  —  /home/docs", "Work  —  /var/work", "Report  —  /home/r.pdf"], page.Rows.Select(r => r.Display));
        Assert.False(model.IsDirty);

        page.SelectedRow = page.Rows[2];
        page.MoveUp();
        Assert.Equal("Report", page.SelectedRow?.Name);
        Assert.True(model.IsDirty);

        page.SelectedRow = page.Rows[0];
        page.Remove();
        Assert.Equal("Report", page.SelectedRow?.Name);

        var renaming = page.RenameAsync();
        var asked = await harness.Dialogs.WaitShownAsync();
        Assert.Equal("Report", asked.InitialText);
        harness.Dialogs.Answer(DialogResult.Ok(" Annual report "));
        await renaming.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(["Annual report", "Work"], page.Rows.Select(r => r.Name));
        Assert.Equal(["Docs", "Work", "Report"], harness.Settings.Hotlist.Current.Items.Select(i => i.Name));

        model.Apply();

        Assert.Equal(["Annual report", "Work"], harness.Settings.Hotlist.Current.Items.Select(i => i.Name));
        Assert.Equal(["/home/r.pdf", "/var/work"], harness.Settings.Hotlist.Current.Items.Select(i => i.Path));
        Assert.False(model.IsDirty);
    }

    [AvaloniaFact]
    public async Task AddSavesATypedNetworkServerUnderItsName()
    {
        using var harness = new SettingsHarness();
        var model = harness.NewView();
        var page = model.Hotlist;

        var adding = page.AddAsync();
        await harness.Dialogs.WaitShownAsync();
        harness.Dialogs.Answer(DialogResult.Ok(@" \\192.168.1.15 "));
        var naming = await harness.Dialogs.WaitShownAsync();
        Assert.Equal("192.168.1.15", naming.InitialText);
        harness.Dialogs.Answer(DialogResult.Ok("192.168.1.15"));
        await adding.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("192.168.1.15", page.SelectedRow?.Name);
        Assert.True(model.IsDirty);
        model.Apply();
        var saved = Assert.Single(harness.Settings.Hotlist.Current.Items);
        Assert.Equal(("192.168.1.15", @"\\192.168.1.15\"), (saved.Name, saved.Path));
    }

    [AvaloniaFact]
    public async Task AddWithNothingTypedAddsNothing()
    {
        using var harness = new SettingsHarness();
        var page = harness.NewView().Hotlist;

        var adding = page.AddAsync();
        await harness.Dialogs.WaitShownAsync();
        harness.Dialogs.Answer(DialogResult.Ok("  "));
        await adding.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Empty(page.Rows);
    }

    [AvaloniaFact]
    public void AnEmptyHotlistSaysSoAndItsButtonsDoNothing()
    {
        using var harness = new SettingsHarness();
        var page = harness.NewView().Hotlist;

        Assert.True(page.IsEmpty);
        Assert.Equal("No favorites yet.", page.EmptyText);
        page.Remove();
        page.MoveDown();
        Assert.Empty(page.Rows);
    }
}
