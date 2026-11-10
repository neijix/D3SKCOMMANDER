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

    [AvaloniaFact]
    [Trait("Tier", "Slow")]
    public async Task LettingGoOfAPlayingVideoStopsItSoItsFileCanBeDeleted()
    {
        Assert.SkipWhen(VlcLocator.Find() is null, "VLC is not installed on this machine");
        using var rig = await Rig.Open(VlcRuntime.Shared);
        await rig.WaitFor(() => rig.Commands.Panel!.ShownVideo is { } shown && Frame(shown) is not null, 1000);
        var clip = Path.Combine(rig.Root, "clip.ts");

        await rig.Commands.LetGoAsync([rig.Root]);
        File.Delete(clip);

        Assert.False(File.Exists(clip));
        Assert.Null(rig.Commands.Panel!.ShownVideo);
    }

    [AvaloniaFact]
    [Trait("Tier", "Slow")]
    public async Task ASoundPlaysUnderItsNameFromTheStartSetting()
    {
        Assert.SkipWhen(VlcLocator.Find() is null, "VLC is not installed on this machine");
        using var rig = await Rig.Open(VlcRuntime.Shared, startSeconds: 3);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        rig.RestOn("tone.wav");

        await rig.WaitFor(() => rig.Commands.Panel!.ShownVideo is { } shown && shown.FilePath.EndsWith("tone.wav", StringComparison.Ordinal) && shown.Position >= TimeSpan.FromSeconds(3), 1000);

        var sound = rig.Commands.Panel!.ShownVideo;
        Assert.NotNull(sound);
        Assert.True(sound.Position >= TimeSpan.FromSeconds(3), $"at {sound.Position}");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"reached after {clock.Elapsed}: it did not jump");
        Assert.Contains(sound.Children.OfType<Avalonia.Controls.TextBlock>(), name => name.Text == "tone.wav");
        Assert.Empty(sound.Children.OfType<Avalonia.Controls.Image>());
    }

    // Silence: 8 kHz, mono, 16-bit PCM.
    private static byte[] Wave(int seconds)
    {
        const int rate = 8000;
        var data = rate * 2 * seconds;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + data);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(data);
        writer.Write(new byte[data]);
        writer.Flush();
        return stream.ToArray();
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

        public static async Task<Rig> Open(Func<LibVLC?> vlc, int startSeconds = 0)
        {
            var root = Directory.CreateTempSubdirectory("d3sk-quick-video-").FullName;
            File.Copy(Clip, Path.Combine(root, "clip.ts"));
            File.WriteAllText(Path.Combine(root, "note.txt"), "text\n");
            File.WriteAllBytes(Path.Combine(root, "tone.wav"), Wave(8));
            var workspace = new PanelWorkspace(new LocalFileSystemProvider(), new FileOps.FileOpsCommandTests.IdlePicker());
            await workspace.OpenAsync(root, root, TestContext.Current.CancellationToken);
            workspace.Activate(PanelSide.Left);
            var rig = new Rig(root, workspace, vlc);
            rig.Commands.StartSecondsOverride = () => startSeconds;
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
