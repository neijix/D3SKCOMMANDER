using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using D3skCommander.App.Features.Jobs;
using D3skCommander.Core.FileSystem;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.App.Tests.Features.Jobs;

public class OperationsBarTests
{
    private static readonly ILocalizer Text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);

    [Fact]
    public void IdleBarShowsNothing()
    {
        var model = new OperationsBarModel(new JobService(), Text);
        model.Refresh();
        Assert.False(model.IsVisible);
    }

    [Fact]
    public async Task RunningJobShowsTextsAndFallsBackToTheTitle()
    {
        var jobs = new JobService();
        var gate = new TaskCompletionSource();
        var reported = new TaskCompletionSource();
        var job = jobs.Start("Copy", async (progress, _) =>
        {
            progress.Report(new JobProgress(2, 5, 2048, 4096, ""));
            reported.SetResult();
            await gate.Task;
        });
        await reported.Task;
        var model = new OperationsBarModel(jobs, Text);
        model.Refresh();

        Assert.True(model.IsVisible);
        Assert.Equal("Copy", model.Title);
        Assert.Equal("Copy", model.Current);
        Assert.Equal("2/5", model.Items);
        Assert.Equal(LocalFileSystem.FormatSize(2048) + " / " + LocalFileSystem.FormatSize(4096), model.Bytes);

        gate.SetResult();
        await job.Completion;
        model.Refresh();
        Assert.False(model.IsVisible);
    }

    [Fact]
    public async Task NewestRunningJobWinsAndCancelStopsIt()
    {
        var jobs = new JobService();
        var gate = new TaskCompletionSource();
        var started = new CountdownEvent(2);
        var first = jobs.Start("First", async (_, _) => { started.Signal(); await gate.Task; });
        var second = jobs.Start("Second", async (progress, token) =>
        {
            progress.Report(new JobProgress(0, 1, 0, 10, "b.txt"));
            started.Signal();
            await Task.Delay(Timeout.Infinite, token);
        });
        Assert.True(started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var model = new OperationsBarModel(jobs, Text);
        model.Refresh();

        Assert.Equal("Second", model.Title);
        Assert.Equal("b.txt", model.Current);

        model.Cancel();
        await second.Completion;
        model.Refresh();
        Assert.Equal("First", model.Title);

        gate.SetResult();
        await first.Completion;
    }

    [AvaloniaFact]
    public async Task ViewFollowsAJobWithoutInputAndCancelButtonCancels()
    {
        var jobs = new JobService();
        var job = jobs.Start("Copy", async (progress, token) =>
        {
            progress.Report(new JobProgress(1, 2, 0, 0, "a.txt"));
            await Task.Delay(Timeout.Infinite, token);
        });
        var view = new OperationsBarView(new OperationsBarModel(jobs, Text));
        var window = new Window { Content = view };
        window.Show();

        for (var i = 0; i < 100 && view.FindControl<TextBlock>("CurrentText")!.Text != "a.txt"; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(view.IsVisible);
        Assert.Equal("a.txt", view.FindControl<TextBlock>("CurrentText")!.Text);
        Assert.Equal(Text.Get("cancel"), view.FindControl<Button>("CancelButton")!.Content);

        view.FindControl<Button>("CancelButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await job.Completion;
        for (var i = 0; i < 100 && view.IsVisible; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.False(view.IsVisible);
        window.Close();
    }
}
