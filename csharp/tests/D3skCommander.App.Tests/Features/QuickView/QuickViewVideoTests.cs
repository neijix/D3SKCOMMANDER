using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Features.QuickView;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Smoke;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Preview;
using D3skCommander.Core.Providers;
using LibVLCSharp.Shared;
using Xunit;

namespace D3skCommander.App.Tests.Features.QuickView;

public class QuickViewVideoTests
{
    private static string Clip => Path.Combine(AppContext.BaseDirectory, "fixtures", "video", "clip.ts");

    [AvaloniaFact]
    public async Task WithoutVlcTheVideoSaysToInstallIt()
    {
        using var rig = await Rig.Open(() => null);

        await rig.WaitFor(() => rig.Commands.Panel!.SummaryText.Length > 0);

        Assert.Null(rig.Commands.Panel!.ShownVideo);
        Assert.Contains("Install VLC", rig.Commands.Panel.SummaryText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    [Trait("Tier", "Slow")]
    public async Task WithVlcTheVideoPlaysSilentlyAndStopsWhenTheCursorMoves()
    {
        Assert.SkipWhen(VlcLocator.Find() is null, "VLC is not installed on this machine");
        using var rig = await Rig.Open(VlcRuntime.Shared);

        await rig.WaitFor(() => rig.Commands.Panel!.ShownVideo is not null, 1000);
        var video = rig.Commands.Panel!.ShownVideo;
        Assert.NotNull(video);
        await rig.WaitFor(() => Frame(video) is not null, 1000);

        Assert.NotNull(Frame(video));
        Assert.False(video.SoundOn);
        rig.RestOn("note.txt");
        await rig.WaitFor(() => rig.Commands.Panel!.ShownVideo is null);
        Assert.Null(rig.Commands.Panel!.ShownVideo);
    }

    private static Bitmap? Frame(VideoPreview video) =>
        video.Children.OfType<Avalonia.Controls.Image>().FirstOrDefault()?.Source as Bitmap;

    private sealed class Rig : IDisposable
    {
        private Rig(string root, PanelWorkspace workspace, Func<LibVLC?> vlc)
        {
            Root = root;
            Workspace = workspace;
            var text = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
            Commands = new QuickViewCommands(workspace, text, new JobService(), Clock) { Vlc = vlc };
        }

        public string Root { get; }

        public PanelWorkspace Workspace { get; }

        public FakeTimeProvider Clock { get; } = new();

        public QuickViewCommands Commands { get; }

        public static async Task<Rig> Open(Func<LibVLC?> vlc)
        {
            var root = Directory.CreateTempSubdirectory("d3sk-quick-video-").FullName;
            File.Copy(Clip, Path.Combine(root, "clip.ts"));
            File.WriteAllText(Path.Combine(root, "note.txt"), "text\n");
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new FileOps.FileOpsCommandTests.IdlePicker());
            await workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            workspace.Activate(PanelSide.Left);
            var rig = new Rig(root, workspace, vlc);
            rig.MoveCursorTo("clip.ts");
            rig.Commands.Toggle();
            return rig;
        }

        public void MoveCursorTo(string name) => QuickViewSteps.MoveCursorTo(Workspace, name);

        public void RestOn(string name) => QuickViewSteps.RestOn(Workspace, Commands, Clock, name);

        public Task WaitFor(Func<bool> done, int tries = 300) => QuickViewSteps.WaitFor(done, tries);

        public void Dispose()
        {
            Commands.Stop();
            Workspace.Dispose();
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
                // The player may still hold the clip for a moment while it stops in the background.
            }
        }
    }
}
