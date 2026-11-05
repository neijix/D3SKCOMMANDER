using D3skCommander.App.Dialogs;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.Core.Tools;
using Xunit;

namespace D3skCommander.App.Tests.Features.Tools;

public class TimeDialogTests
{
    [Fact]
    public async Task AddTimeShiftsTheModificationTime()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("a.txt", "alpha");
        var path = Path.Combine(harness.Left, "a.txt");
        var before = File.GetLastWriteTimeUtc(path);
        harness.Select(PanelSide.Left, "a.txt");

        var pending = harness.Commands.AddTimeAsync();
        var shown = await dialogs.WaitShownAsync();
        var form = shown.Form!;
        ToolHarness.Draw(form);
        Assert.Equal(harness.Text.Get("cmd.addtime"), form.Title);
        Assert.Equal(path, form.Fields[0].Label);
        var offset = Assert.IsType<TextDialogField>(form.Fields[1]);
        Assert.Equal(harness.Text.Get("dlg.offset"), offset.Label);
        Assert.Equal("+1h", offset.Text);
        offset.Text = "+2h";
        var modified = Assert.IsType<CheckDialogField>(form.Fields[2]);
        var accessed = Assert.IsType<CheckDialogField>(form.Fields[3]);
        Assert.Equal(harness.Text.Get("dlg.modified"), modified.Label);
        Assert.True(modified.IsChecked);
        Assert.Equal(harness.Text.Get("dlg.accessed"), accessed.Label);
        Assert.False(accessed.IsChecked);
        Assert.Empty(form.Fields.OfType<FolderDialogField>());
        dialogs.Answer(DialogResult.Ok());
        await pending;

        var delta = (File.GetLastWriteTimeUtc(path) - before).TotalSeconds;
        Assert.InRange(delta, 7198, 7202);
        Assert.NotEmpty(harness.Jobs.Jobs);
    }

    [Fact]
    public async Task ReDateStepsEachFile()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("a.txt", "alpha");
        await harness.AddFileAsync("b.txt", "beta");
        harness.Select(PanelSide.Left, "a.txt", "b.txt");

        var pending = harness.Commands.ReDateAsync();
        var shown = await dialogs.WaitShownAsync();
        var form = shown.Form!;
        ToolHarness.Draw(form);
        Assert.Equal(harness.Text.Get("cmd.redate"), form.Title);
        Assert.Equal("2 " + harness.Text.Get("files"), form.Fields[0].Label);
        var start = Assert.IsType<TextDialogField>(form.Fields[1]);
        Assert.Equal(harness.Text.Get("dlg.start_date"), start.Label);
        start.Text = "2020-01-01 00:00:00";
        var step = Assert.IsType<TextDialogField>(form.Fields[2]);
        Assert.Equal(harness.Text.Get("dlg.step"), step.Label);
        Assert.Equal("60s", step.Text);
        Assert.DoesNotContain(form.Fields, field => field is CheckDialogField);
        dialogs.Answer(DialogResult.Ok());
        await pending;

        var parsed = ToolInput.ParseDate("2020-01-01 00:00:00");
        Assert.True(parsed.IsSuccess);
        var times = new[]
        {
            File.GetLastWriteTimeUtc(Path.Combine(harness.Left, "a.txt")),
            File.GetLastWriteTimeUtc(Path.Combine(harness.Left, "b.txt")),
        };
        Array.Sort(times);
        AssertClose(times[0], parsed.Time.UtcDateTime);
        AssertClose(times[1], parsed.Time.UtcDateTime.AddSeconds(60));
    }

    [Fact]
    public async Task TouchSetsTheDate()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("a.txt", "alpha");
        var path = Path.Combine(harness.Left, "a.txt");
        harness.Select(PanelSide.Left, "a.txt");

        var pending = harness.Commands.TouchAsync();
        var shown = await dialogs.WaitShownAsync();
        var form = shown.Form!;
        ToolHarness.Draw(form);
        Assert.Equal(harness.Text.Get("cmd.touch"), form.Title);
        var value = Assert.IsType<TextDialogField>(form.Fields[1]);
        Assert.Equal(harness.Text.Get("date"), value.Label);
        value.Text = "2020-06-07 08:09:10";
        dialogs.Answer(DialogResult.Ok());
        await pending;

        var parsed = ToolInput.ParseDate("2020-06-07 08:09:10");
        Assert.True(parsed.IsSuccess);
        AssertClose(File.GetLastWriteTimeUtc(path), parsed.Time.UtcDateTime);
    }

    [Fact]
    public async Task TouchOnAFolderChangesTheFilesInside()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync(Path.Combine("sub", "a.txt"), "alpha");
        await harness.AddFileAsync(Path.Combine("sub", "b.txt"), "beta");
        await harness.AddFileAsync("c.txt", "side");
        var outside = Path.Combine(harness.Left, "c.txt");
        var old = new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(outside, old);
        File.SetLastWriteTimeUtc(Path.Combine(harness.Left, "sub", "a.txt"), old);
        File.SetLastWriteTimeUtc(Path.Combine(harness.Left, "sub", "b.txt"), old);
        harness.Select(PanelSide.Left, "sub");

        var pending = harness.Commands.TouchAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.Equal("2 " + harness.Text.Get("files"), shown.Form!.Fields[0].Label);
        Assert.IsType<TextDialogField>(shown.Form.Fields[1]).Text = "2019-05-06 07:08:09";
        ToolHarness.Draw(shown.Form);
        dialogs.Answer(DialogResult.Ok());
        await pending;

        var parsed = ToolInput.ParseDate("2019-05-06 07:08:09");
        Assert.True(parsed.IsSuccess);
        AssertClose(File.GetLastWriteTimeUtc(Path.Combine(harness.Left, "sub", "a.txt")), parsed.Time.UtcDateTime);
        AssertClose(File.GetLastWriteTimeUtc(Path.Combine(harness.Left, "sub", "b.txt")), parsed.Time.UtcDateTime);
        AssertClose(File.GetLastWriteTimeUtc(outside), old);
    }

    [Fact]
    public async Task ABadDateDoesNotStartAJob()
    {
        var dialogs = new FakeDialogService();
        using var harness = await ToolHarness.Open(dialogs);
        await harness.AddFileAsync("a.txt", "alpha");
        var path = Path.Combine(harness.Left, "a.txt");
        var before = File.GetLastWriteTimeUtc(path);
        harness.Select(PanelSide.Left, "a.txt");

        var pending = harness.Commands.TouchAsync();
        var shown = await dialogs.WaitShownAsync();
        Assert.IsType<TextDialogField>(shown.Form!.Fields[1]).Text = "yesterday";
        dialogs.Answer(DialogResult.Ok());
        var error = await dialogs.WaitShownAsync();
        Assert.Equal(DialogKind.Message, error.Kind);
        Assert.Contains("Invalid date", error.Text);
        dialogs.Answer(DialogResult.Ok());
        await pending;

        Assert.Empty(harness.Jobs.Jobs);
        AssertClose(File.GetLastWriteTimeUtc(path), before);
    }

    private static void AssertClose(DateTime actual, DateTime expected)
    {
        var delta = actual - expected;
        if (delta < TimeSpan.Zero)
        {
            delta = -delta;
        }

        Assert.True(delta < TimeSpan.FromSeconds(2), actual.ToString("o") + " vs " + expected.ToString("o"));
    }
}
