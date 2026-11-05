using System.Numerics;
using Avalonia.Headless.XUnit;
using D3skCommander.App.Features.Compare;
using D3skCommander.App.Host;
using D3skCommander.Core.Compare;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using Hexa.NET.ImGui;
using Xunit;

namespace D3skCommander.App.Tests.Features.Compare;

public class CompareWindowTests
{
    [AvaloniaFact]
    public void WindowRendersTextBinaryAndEditModes()
    {
        using var dir = new TempDir();
        var left = Path.Combine(dir.Path, "a.txt");
        var right = Path.Combine(dir.Path, "b.txt");
        File.WriteAllBytes(left, "x\ny\nz\n"u8.ToArray());
        File.WriteAllBytes(right, "x\nY\nz\nw\n"u8.ToArray());
        var jobs = new JobService();
        var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        var view = new CompareView(left, right, jobs, text, null);

        Until(view, () => view.Surface == CompareSurface.Text);
        Assert.Equal(CompareSurface.Text, view.Surface);
        Assert.True(view.DrewNext);
        Assert.Contains("Y", view.DrawnRows);
        Assert.Contains("w", view.DrawnRows);
        Assert.Equal(CompareText.Fill(text.Get("compare.blocks"), "{n}", "2"), view.SurfaceText);
        AssertTitled(jobs, text.Get("tool.compare.title"));

        view.NextBlock();
        Draw(view);
        Assert.Equal(1, view.Session.Current);

        view.BeginEdit();
        Draw(view);
        Assert.Equal(CompareSurface.Edit, view.Surface);
        Assert.True(view.DrewSave);
        Assert.Equal(left, view.SurfaceText);

        view.Session.Save(0);
        Draw(view);
        Assert.Equal(CompareText.Fill(text.Get("compare.status.saved"), "{path}", left), view.StatusLine);

        view.ShowCompareView();
        Draw(view);
        Assert.Equal(CompareSurface.Text, view.Surface);

        var binaryLeft = Path.Combine(dir.Path, "a.bin");
        var binaryRight = Path.Combine(dir.Path, "b.bin");
        File.WriteAllBytes(binaryLeft, [0, 1, 2, 3]);
        File.WriteAllBytes(binaryRight, [0, 1, 9, 3]);
        var binary = new CompareView(binaryLeft, binaryRight, jobs, text, null);
        Until(binary, () => binary.Surface == CompareSurface.Binary);
        Assert.Equal(CompareSurface.Binary, binary.Surface);
        Assert.Equal(CompareText.Offset(text, 2), binary.SurfaceText);
        Assert.True(binary.DrewPeek);
        AssertTitled(jobs, text.Get("tool.compare.title"));
    }

    [AvaloniaFact]
    public void StatusKindIsTranslated()
    {
        var text = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);
        Assert.Equal("Enregistré a.txt", CompareText.Status(text, CompareStatus.Saved("a.txt")));
        Assert.Contains("4 Mio", CompareText.Status(text, CompareStatus.TooLarge("b.txt")), StringComparison.Ordinal);
        Assert.Equal("Erreur : disk", CompareText.Status(text, CompareStatus.Error("disk")));
        Assert.Equal("", CompareText.Status(text, CompareStatus.Empty));
    }

    [AvaloniaFact]
    public void TheJobTitleFollowsTheLanguage()
    {
        var jobs = new JobService();
        var text = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);
        var view = new CompareView("missing-left", "missing-right", jobs, text, null);
        Draw(view);
        var job = Assert.Single(jobs.Jobs);
        Assert.Equal(text.Get("tool.compare.title"), job.Title);
        Assert.Equal("Comparer les fichiers", job.Title);
        Assert.NotEqual("Compare", job.Title);
    }

    private static void AssertTitled(JobService jobs, string title)
    {
        Assert.NotEmpty(jobs.Jobs);
        foreach (var job in jobs.Jobs)
        {
            Assert.Equal(title, job.Title);
            Assert.NotEqual("Compare", job.Title);
        }
    }

    private static void Until(CompareView view, Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < end)
        {
            Draw(view);
            if (ready())
            {
                return;
            }

            Thread.Sleep(5);
        }

        Draw(view);
    }

    private static void Draw(CompareView view) => ImGuiProbe.Draw(() => Frame(view));

    private static void Frame(CompareView view)
    {
        ImGui.SetNextWindowSize(new Vector2(1000, 640), ImGuiCond.Always);
        ImGui.Begin("compare-probe");
        try
        {
            view.Draw();
        }
        finally
        {
            ImGui.End();
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = Directory.CreateTempSubdirectory("d3sk-compare-").FullName;

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
