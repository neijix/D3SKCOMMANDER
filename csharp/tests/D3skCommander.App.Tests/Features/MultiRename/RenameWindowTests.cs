using System.Numerics;
using Avalonia.Headless.XUnit;
using D3skCommander.App;
using D3skCommander.App.Features.MultiRename;
using D3skCommander.App.Host;
using D3skCommander.App.Shell;
using D3skCommander.Core.Commands;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.MultiRename;
using Hexa.NET.ImGui;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Engine = D3skCommander.Core.MultiRename.MultiRename;

namespace D3skCommander.App.Tests.Features.MultiRename;

public class RenameWindowTests
{
    [AvaloniaFact]
    public void EveryTokenButtonExpands()
    {
        using var dir = new WorkDir();
        var folder = Path.Combine(dir.Root, "parent");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "report.txt");
        File.WriteAllText(file, "x");
        var text = Localizer.Load(Language.En, Language.En);
        var view = new RenameWindow(text, new JobService());
        view.Load([file]);
        Assert.Equal(RenameToken.All.Count, DrawnTokens(view));

        foreach (var token in RenameToken.All)
        {
            var label = text.Get(token.LabelKey);
            view.Session.Rule.NameMask = "";
            view.Session.Refresh();
            Click(view, window => Find(window, label));
            Assert.True(view.Session.Rule.NameMask == label, label + " inserted '" + view.Session.Rule.NameMask + "'");
            var expected = Engine.NewName(file, 0, view.Session.Rule);
            var preview = Assert.Single(view.Session.Previews);
            Assert.Null(preview.Error);
            Assert.Equal(expected, Path.GetFileName(preview.To));
            Assert.Equal(expected, view.Preview.CellText(0, 1));
            Assert.Equal("report.txt", view.Preview.CellText(0, 0));
        }

        Assert.Equal(text.Get("rename.col.old"), view.Preview.HeaderText("rename.col.old"));
        Assert.Equal(text.Get("rename.col.new"), view.Preview.HeaderText("rename.col.new"));
    }

    [AvaloniaFact]
    public void PreviewErrorBlocksTheStartButton()
    {
        using var dir = new WorkDir();
        var first = dir.Touch("a.txt");
        var second = dir.Touch("b.txt");
        var jobs = new JobService();
        var view = new RenameWindow(Localizer.Load(Language.En, Language.En), jobs);
        view.Load([first, second]);
        view.Session.Rule.NameMask = "same";
        view.Session.Refresh();
        ImGuiProbe.Draw(() => Draw(view));
        Assert.False(view.StartEnabled);
        Assert.False(string.IsNullOrEmpty(view.Preview.CellText(0, 2)));
        Click(view, window => window.StartCenter);
        Assert.Empty(jobs.Jobs);
        Assert.Empty(view.Session.Done);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [AvaloniaFact]
    public async Task StartThenUndoRenamesAndRestores()
    {
        using var dir = new WorkDir();
        var jobs = new JobService();
        var view = new RenameWindow(Localizer.Load(Language.En, Language.En), jobs);
        var first = dir.Touch("a.txt");
        var second = dir.Touch("b.txt");
        view.Load([first, second]);
        view.Session.Rule.NameMask = "n[C]";
        view.Session.Refresh();
        Click(view, window => window.StartCenter);
        var started = Assert.Single(jobs.Jobs);
        var applied = await started.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, applied.Outcome);
        await Idle(view);
        Assert.True(File.Exists(dir.PathOf("n1.txt")));
        Assert.True(File.Exists(dir.PathOf("n2.txt")));
        Click(view, window => window.UndoCenter);
        Assert.Collection(jobs.Jobs, _ => { }, _ => { });
        var restored = await jobs.Jobs[1].Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Completed, restored.Outcome);
        await Idle(view);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.False(view.Session.CanUndo);
    }

    [AvaloniaFact]
    public void ModuleRegistersTheCommand()
    {
        var text = Localizer.Load(Language.En, Language.En);
        using var provider = Program.BuildProvider(text);
        var module = provider.GetRequiredService<MultiRenameModule>();
        var shell = new ShellContributions();
        module.Contribute(shell);
        Assert.True(shell.CanExecute(CommandId.MultiRename));
    }

    private static int DrawnTokens(RenameWindow view)
    {
        ImGuiProbe.Draw(() => Draw(view));
        return view.TokenButtons.Count;
    }

    private static void Click(RenameWindow view, Func<RenameWindow, Vector2> point)
    {
        Vector2 at = default;
        var draws = 0;
        ImGuiProbe.Frames(4, frame =>
        {
            if (draws < 2)
            {
                return;
            }

            var io = ImGui.GetIO();
            io.AddMousePosEvent(at.X, at.Y);
            if (frame == 2)
            {
                io.AddMouseButtonEvent(0, true);
            }

            if (frame == 3)
            {
                io.AddMouseButtonEvent(0, false);
            }
        }, () =>
        {
            Draw(view);
            draws++;
            if (draws == 2)
            {
                at = point(view);
            }
        });

        Assert.True(at.X > 1 && at.Y > 1, "the button was not drawn");
    }

    private static Vector2 Find(RenameWindow view, string label)
    {
        foreach (var hit in view.TokenButtons)
        {
            if (hit.Token == label)
            {
                return hit.Center;
            }
        }

        return Vector2.Zero;
    }

    private static void Draw(RenameWindow view)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize, ImGuiCond.Always);
        var open = true;
        if (ImGui.Begin("rename-test", ref open))
        {
            view.Draw();
        }

        ImGui.End();
    }

    private static async Task Idle(RenameWindow view)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (view.IsBusy)
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The rename job did not finish.");
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class WorkDir : IDisposable
    {
        public WorkDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "d3sk-rename-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Touch(string name)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, "x");
            return path;
        }

        public string PathOf(string name) => Path.Combine(Root, name);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
