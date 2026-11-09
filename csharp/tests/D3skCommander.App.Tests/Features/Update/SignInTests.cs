using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using D3sk.Update.Keychain;
using D3skCommander.App.Features.Update;
using D3skCommander.App.Features.Update.SignIn;
using D3skCommander.App.Shell;
using D3skCommander.App.Startup;
using D3skCommander.App.ToolWindows;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Settings;
using D3skCommander.Core.SettingsLocation;
using D3skCommander.Core.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace D3skCommander.App.Tests.Features.Update;

/// <summary>The Update window's GitHub sign-in against a fake OAuth/API server replaying <c>tests/fixtures/github-oauth</c>.</summary>
public class SignInTests
{
    private const string AccessEntry = "d3sk/d3skcommander/release-token";
    private const string RefreshEntry = "d3sk/d3skcommander/release-refresh-token";

    private static readonly Localizer English = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
    private static readonly Localizer French = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);

    [AvaloniaFact]
    public async Task TheButtonShowsAfterAQuietCheckAndOnlyItStartsTheFlow()
    {
        var rig = new Rig();
        var commands = rig.Commands();

        await commands.CheckAtStartAsync();

        Assert.Equal(English.Get("update.sign_in_needed"), commands.MenuItem.Header);
        Assert.DoesNotContain(rig.Github.Seen, s => s.StartsWith("POST", StringComparison.Ordinal));
        Assert.Empty(rig.Opener.Opened);

        var window = rig.Show(commands.CreateView());
        await WaitFor(() => Find<Button>(window, "SignInButton").IsVisible);
        Assert.Equal(English.Get("update.sign_in"), Find<Button>(window, "SignInButton").Content);
        Assert.Empty(rig.Opener.Opened);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheCodeIsShownTheDevicePageOpenedAndTheTokenPairStoredInTheKeychain()
    {
        var rig = new Rig();
        var model = rig.Commands().CreateView();
        var window = rig.Show(model);
        await WaitFor(() => model.SignInPanel!.IsOffered);

        await model.SignInPanel!.SignInAsync();

        Assert.Equal(["https://github.com/login/device"], rig.Opener.Opened);
        Assert.Equal("Enter the code WDJB-MJHT on https://github.com/login/device.", model.SignInPanel.Line);
        Assert.False(model.SignInPanel.IsOffered);
        await Advance(rig.Time, () => rig.Keychain.Get(RefreshEntry) is not null);
        Assert.Equal("ghu_FAKEaccess0123456789", rig.Keychain.Get(AccessEntry));
        Assert.Equal("ghr_FAKErefresh0123456789", rig.Keychain.Get(RefreshEntry));
        await WaitFor(() => model.Status == English.Get("update.up_to_date"));
        Assert.False(model.SignInPanel.IsOffered);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheCodeLineIsFrench()
    {
        var rig = new Rig();
        var model = rig.Commands(French).CreateView();
        var window = rig.Show(model);
        await WaitFor(() => model.SignInPanel!.IsOffered);

        await model.SignInPanel!.SignInAsync();

        Assert.Equal("Saisissez le code WDJB-MJHT sur https://github.com/login/device.", model.SignInPanel.Line);
        Assert.Equal("Se connecter à GitHub", model.SignInPanel.ButtonText);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowMidPollCancelsTheJobAndNoRequestFollows()
    {
        var rig = new Rig();
        var model = rig.Commands().CreateView();
        var window = rig.Show(model);
        await WaitFor(() => model.SignInPanel!.IsOffered);
        await model.SignInPanel!.SignInAsync();
        await Advance(rig.Time, () => rig.Github.Count("oauth/access_token") >= 1);
        var job = Assert.IsAssignableFrom<IJob>(model.SignInPanel.Flow.Job);
        Assert.True(job.IsRunning);

        window.Close();

        var result = await job.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(JobOutcome.Cancelled, result.Outcome);
        var before = rig.Github.Seen.Count;
        for (var i = 0; i < 5; i++)
        {
            rig.Time.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal(before, rig.Github.Seen.Count);
        Assert.Null(rig.Keychain.Get(AccessEntry));
        Assert.Null(rig.Keychain.Get(RefreshEntry));
    }

    [AvaloniaFact]
    public async Task TheSignInJobIsInTheAppsJobService()
    {
        var rig = new Rig();
        var jobs = new JobService();
        var folder = Directory.CreateTempSubdirectory("d3sk-signin-").FullName;
        using var settings = new AppSettings(new SettingsFolder(folder, SettingsSource.Env));
        var services = new ServiceCollection();
        services.AddSingleton<IJobService>(jobs);
        services.AddSingleton<IToolWindowHost>(new NoTools());
        services.AddSingleton<ILocalizer>(English);
        services.AddSingleton(settings);
        services.AddSingleton<IUrlOpener>(rig.Opener);
        UpdateModule.Register(services);
        services.AddSingleton<IUpdateRestarter>(new RecordingRestarter());
        services.AddSingleton(rig.Service);
        services.AddSingleton(rig.Environment);
        using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<UpdateCommands>().CreateView();
        var window = rig.Show(model);
        await WaitFor(() => model.SignInPanel!.IsOffered);

        await model.SignInPanel!.SignInAsync();

        Assert.Contains(model.SignInPanel.Flow.Job, jobs.Jobs);
        window.Close();
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static async Task WaitFor(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(done());
    }

    // The poll timer fires when the fake clock reaches it: move on in steps until the condition holds.
    private static async Task Advance(ManualTime time, Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!done() && DateTime.UtcNow < until)
        {
            time.Advance(TimeSpan.FromSeconds(6));
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.True(done());
    }

    private sealed class Rig
    {
        private readonly UpdateService _service;
        private readonly UpdateEnvironment _environment = Releases.Environment();

        public Rig()
        {
            _service = new UpdateService(
                new JobService(),
                Core.Update.UpdateConfig.Create(_environment.Version, version => UpdateChecker.AssetFor(_environment, version)),
                new HttpClient(Github),
                Keychain,
                setting: _ => null,
                time: Time);
        }

        public UpdateService Service => _service;

        public UpdateEnvironment Environment => _environment;

        public ManualTime Time { get; } = new();

        public MemoryKeychain Keychain { get; } = new();

        public RecordingOpener Opener { get; } = new();

        public FakeOAuthServer Github { get; } = new();

        public UpdateCommands Commands(Localizer? text = null)
        {
            var folder = Directory.CreateTempSubdirectory("d3sk-signin-").FullName;
            return new UpdateCommands(
                new UpdateChecker(_service, _environment),
                _service,
                new RecordingRestarter(),
                new NoTools(),
                text ?? English,
                new PreferencesStore(folder),
                () => false,
                () => false,
                new JobService(),
                Opener);
        }

        public Window Show(UpdateViewModel model)
        {
            var window = new Window { Content = model.CreateContent() };
            window.Show();
            return window;
        }
    }

    private sealed class RecordingOpener : IUrlOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string url) => Opened.Add(url);
    }

    private sealed class NoTools : IToolWindowHost
    {
        public void Register(ToolKind kind, Func<IShellWidget> content)
        {
        }

        public ToolWindow Open(ToolKind kind) => throw new NotSupportedException();

        public bool IsOpen(ToolKind kind) => false;
    }
}

/// <summary>The private repository (404 without a token), the device flow and the user lookup, answering the recorded samples.</summary>
internal sealed class FakeOAuthServer : HttpMessageHandler
{
    private readonly List<string> _seen = [];
    private int _polls;

    public IReadOnlyList<string> Seen
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    public int Count(string part) => Seen.Count(s => s.Contains(part, StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.GetLeftPart(UriPartial.Path);
        lock (_seen)
        {
            _seen.Add(request.Method.Method + " " + url);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var signedIn = request.Headers.Authorization?.Parameter == "ghu_FAKEaccess0123456789";
        return Task.FromResult(url switch
        {
            "https://api.github.com/repos/neijix/D3SKCOMMANDER/releases" when signedIn => Reply(HttpStatusCode.OK, "[]"),
            "https://api.github.com/repos/neijix/D3SKCOMMANDER/releases" => Reply(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}"),
            "https://github.com/login/device/code" => Reply(HttpStatusCode.OK, Fixture("device-code.json")),
            "https://github.com/login/oauth/access_token" =>
                Reply(HttpStatusCode.OK, Fixture(Interlocked.Increment(ref _polls) < 3 ? "authorization-pending.json" : "token.json")),
            "https://api.github.com/user" => Reply(HttpStatusCode.OK, Fixture("user.json")),
            _ => Reply(HttpStatusCode.NotFound, "{}"),
        });
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "github-oauth", "2022-11-28", name));
}

/// <summary>A clock that moves only when the test says so: the sign-in's poll timers fire on <see cref="Advance"/>.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_timers)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state);
        lock (_timers)
        {
            timer.Change(dueTime, period, _now);
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_timers)
        {
            _now += by;
            due = [.. _timers.Where(t => t.IsDue(_now))];
            foreach (var timer in due)
            {
                timer.Arm(_now);
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period) => Change(dueTime, period, DateTimeOffset.MinValue);

        public bool Change(TimeSpan dueTime, TimeSpan period, DateTimeOffset now)
        {
            _period = period;
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
            return true;
        }

        public bool IsDue(DateTimeOffset now) => _due is { } due && due <= now;

        public void Arm(DateTimeOffset now) => _due = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : now + _period;

        public void Fire() => callback(state);

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
