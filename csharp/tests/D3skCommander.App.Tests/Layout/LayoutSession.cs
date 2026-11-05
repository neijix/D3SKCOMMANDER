using System.Globalization;
using Avalonia.Headless;
using D3skCommander.App.Dialogs;
using D3skCommander.App.Features.Panels;
using D3skCommander.App.Shell;
using D3skCommander.App.Tests.Dialogs;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using TextCatalog = D3skCommander.Core.Localization.Localizer;

namespace D3skCommander.App.Tests.Layout;

/// <summary>The v0.1 sample folder, opened in both panels, in a headless main window.</summary>
internal sealed class LayoutSession : IDisposable
{
    public static readonly (double Width, double Height)[] Sizes =
    [
        (640, 400),
        (1024, 640),
        (1280, 800),
        (1920, 1080),
    ];

    private readonly ServiceProvider _provider;
    private readonly string _directory;
    private bool _closed;

    private LayoutSession(ServiceProvider provider, string directory, FakeFolderPicker picker)
    {
        _provider = provider;
        _directory = directory;
        Picker = picker;
        Localizer = provider.GetRequiredService<ILocalizer>();
        Panels = provider.GetRequiredService<PanelWorkspace>();
        Tools = provider.GetRequiredService<IToolWindowHost>();
        Window = provider.GetRequiredService<MainWindow>();
    }

    public ILocalizer Localizer { get; }

    public PanelWorkspace Panels { get; }

    public IToolWindowHost Tools { get; }

    public MainWindow Window { get; }

    public FakeFolderPicker Picker { get; }

    public string Directory => _directory;

    public static async Task<LayoutSession> Open(CancellationToken cancellationToken)
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("d3sk-layout-").FullName;
        var picker = new FakeFolderPicker();
        var localizer = TextCatalog.Load(Language.En, Language.En);
        var provider = Program.BuildProvider(localizer, services =>
        {
            services.AddSingleton<IFolderPicker>(picker);
            services.AddSingleton<TimeProvider>(new FixedClock());
        });
        var session = new LayoutSession(provider, directory, picker);
        try
        {
            WriteSample(directory);
            await session.Panels.OpenAsync(directory, directory, cancellationToken);
            session.Window.Show();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public PanelSnapshot Read(PanelSide side) => Panels.Read(side);

    public string Status(PanelSide side)
    {
        var snap = Read(side);
        return Localizer.Get("panels.selected")
            .Replace("{0}", snap.Selected.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{1}", snap.Selectable.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    public bool Marks(PanelSide side, string fragment)
    {
        foreach (var path in Read(side).Marked)
        {
            if (path.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public bool Lists(PanelSide side, string fragment)
    {
        foreach (var entry in Read(side).Entries)
        {
            if (entry.Name.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Sizes the window in device-independent pixels at a render scale, then lays it out.</summary>
    public void Resize(double width, double height, double scale)
    {
        Window.SetRenderScaling(scale);
        Window.Width = width;
        Window.Height = height;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    public async Task WaitUntil(Func<bool> ready, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (!ready())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("The panel did not update.");
            }

            await Task.Delay(20, cancellationToken);
        }
    }

    public void Dispose()
    {
        if (!_closed && Window.IsVisible)
        {
            Window.Close();
        }

        _closed = true;
        _provider.Dispose();
        if (System.IO.Directory.Exists(_directory))
        {
            System.IO.Directory.Delete(_directory, true);
        }
    }

    private static void WriteSample(string directory)
    {
        for (var i = 0; i < 40; i++)
        {
            var name = "file number " + i.ToString(CultureInfo.InvariantCulture) + " with a rather long name.txt";
            File.WriteAllText(Path.Combine(directory, name), "x");
        }

        System.IO.Directory.CreateDirectory(Path.Combine(directory, "a folder"));
    }

    /// <summary>Holds the panel refresh still so a long press is not a reload.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
