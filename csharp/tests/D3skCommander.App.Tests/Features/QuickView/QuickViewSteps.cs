using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickView;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Smoke;
using Xunit;

namespace D3skCommander.App.Tests.Features.QuickView;

/// <summary>Steps the quick view tests share: move the left cursor, let it rest, wait for the UI.</summary>
internal static class QuickViewSteps
{
    public static void MoveCursorTo(PanelWorkspace workspace, string name)
    {
        var snap = workspace.Read(PanelSide.Left);
        var index = snap.Entries.ToList().FindIndex(entry => entry.Name == name);
        workspace.ApplySelection(PanelSide.Left, index, []);
    }

    /// <summary>Moves the cursor and lets it rest past the debounce, so the view follows.</summary>
    public static void RestOn(PanelWorkspace workspace, QuickViewCommands commands, FakeTimeProvider clock, string name)
    {
        MoveCursorTo(workspace, name);
        commands.Tick();
        clock.Advance(QuickViewCommands.Debounce + TimeSpan.FromMilliseconds(10));
        commands.Tick();
    }

    public static async Task WaitFor(Func<bool> done, int tries = 300)
    {
        for (var i = 0; i < tries && !done(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}
