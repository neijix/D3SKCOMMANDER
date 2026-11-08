using System.Net;
using D3sk.Update.Keychain;
using D3sk.Update.SignIn;
using D3sk.Update.UpdateClient;
using D3skCommander.Core.Jobs;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Update;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using AppConfig = D3skCommander.Core.Update.UpdateConfig;
using LibraryConfig = D3sk.Update.UpdateClient.UpdateConfig;

namespace D3skCommander.Core.Tests.Update;

/// <summary>
/// Sign-in and token refresh through <see cref="UpdateService"/>, against fake OAuth and API servers that replay the
/// recorded answers in <c>tests/fixtures/github-oauth/2022-11-28</c>.
/// </summary>
public class SignInTests
{
    private const string Api = "https://api.github.com/repos/neijix/D3SKCOMMANDER/releases";
    private const string Login = "https://github.com/login/";
    private const string AccessEntry = "d3sk/d3skcommander/release-token";
    private const string RefreshEntry = "d3sk/d3skcommander/release-refresh-token";
    private const string StaleToken = "ghu_FAKEstale";
    private const string StaleRefresh = "ghr_FAKEstale";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AQuietCheckOfAPrivateRepositoryNeedsSignInAndNeverStartsTheDeviceFlow()
    {
        var github = new FakeGitHub().On("GET", Api, HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");
        using var service = Create(github, new MemoryKeychain());

        var check = await service.CheckAsync("0.2.0", Ct);
        var quiet = await service.StartSignInAsync(userInitiated: false, Ct);

        Assert.Equal(CheckStatus.SignInNeeded, check.Status);
        Assert.NotEqual(SignInPhase.Waiting, quiet.Phase);
        Assert.DoesNotContain(github.Seen, s => s.StartsWith("POST " + Login, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDeviceCodeIsShownAndTheTokenPairIsStoredInTheKeychain()
    {
        var keychain = new MemoryKeychain();
        var time = new FakeTimeProvider();
        var github = new FakeGitHub()
            .On("POST", Login + "device/code", HttpStatusCode.OK, Fixture("device-code.json"));
        var polls = 0;
        github.On("POST", Login + "oauth/access_token", _ => Reply(
            HttpStatusCode.OK,
            Fixture(++polls == 1 ? "authorization-pending.json" : "token.json")));
        github.On("GET", "https://api.github.com/user", HttpStatusCode.OK, Fixture("user.json"));
        using var service = Create(github, keychain, time);

        var started = await service.StartSignInAsync(userInitiated: true, Ct);

        Assert.Equal(SignInPhase.Waiting, started.Phase);
        Assert.Equal("WDJB-MJHT", started.UserCode);
        Assert.True(SignInPolicy.MayOpen(SignInPolicy.VerificationUrl));
        await WaitForAsync(time, () => service.GetSignInState().Phase == SignInPhase.SignedIn);
        Assert.Equal("ghu_FAKEaccess0123456789", keychain.Get(AccessEntry));
        Assert.Equal("ghr_FAKErefresh0123456789", keychain.Get(RefreshEntry));
        Assert.Equal(2, polls);
    }

    [Fact]
    public async Task A401RefreshesOnceAndTheCheckThenSucceeds()
    {
        var keychain = Stale();
        var github = new FakeGitHub();
        github.On("GET", Api, request => Reply(
            request.Headers.Authorization?.Parameter == StaleToken ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            request.Headers.Authorization?.Parameter == StaleToken ? Fixture("api-401.json") : "[]"));
        github.On("POST", Login + "oauth/access_token", HttpStatusCode.OK, Fixture("refresh.json"));
        using var service = Create(github, keychain);

        var check = await service.CheckAsync("0.2.0", Ct);

        Assert.Equal(CheckStatus.UpToDate, check.Status);
        Assert.Equal(1, github.Count("POST", Login + "oauth/access_token"));
        Assert.Equal(2, github.Count("GET", Api));
        Assert.Equal("ghu_FAKEaccess9876543210", keychain.Get(AccessEntry));
        Assert.Equal("ghr_FAKErefresh9876543210", keychain.Get(RefreshEntry));
    }

    [Fact]
    public async Task ASecond401AsksForSignInAgainAfterOneRefresh()
    {
        var keychain = Stale();
        var github = new FakeGitHub()
            .On("GET", Api, HttpStatusCode.Unauthorized, Fixture("api-401.json"))
            .On("POST", Login + "oauth/access_token", HttpStatusCode.OK, Fixture("refresh.json"));
        using var service = Create(github, keychain);

        var check = await service.CheckAsync("0.2.0", Ct);

        Assert.Equal(CheckStatus.SignInNeeded, check.Status);
        Assert.Equal(1, github.Count("POST", Login + "oauth/access_token"));
        Assert.Null(keychain.Get(AccessEntry));
        Assert.Null(keychain.Get(RefreshEntry));
    }

    [Fact]
    public async Task ARejectedRefreshAsksForSignIn()
    {
        var keychain = Stale();
        var github = new FakeGitHub()
            .On("GET", Api, HttpStatusCode.Unauthorized, Fixture("api-401.json"))
            .On("POST", Login + "oauth/access_token", HttpStatusCode.OK, Fixture("refresh-rejected.json"));
        using var service = Create(github, keychain);

        var check = await service.CheckAsync("0.2.0", Ct);

        Assert.Equal(CheckStatus.SignInNeeded, check.Status);
        Assert.Equal(1, github.Count("GET", Api));
    }

    [Fact]
    public async Task NoNetworkIsItsOwnState()
    {
        using var service = Create(new FakeGitHub(), new MemoryKeychain());

        var check = await service.CheckAsync("0.2.0", Ct);

        Assert.Equal(CheckStatus.NoNetwork, check.Status);
    }

    [Fact]
    public async Task AKeychainThatFailsIsReportedWithItsReasonAndNothingIsWritten()
    {
        var keychain = new ThrowingKeychain();
        var github = new FakeGitHub().On("GET", Api, HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");
        using var service = Create(github, keychain);

        var check = await service.CheckAsync("0.2.0", Ct);
        var again = await service.CheckAsync("0.2.0", Ct);

        Assert.Equal(CheckStatus.KeychainUnavailable, check.Status);
        Assert.Equal("no secret service", check.KeychainReason);
        Assert.Equal(CheckStatus.KeychainUnavailable, again.Status);
        Assert.Equal(0, keychain.Writes);
        Assert.NotEqual(SignInPhase.SignedIn, service.GetSignInState().Phase);
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        Assert.Equal(
            "The system keychain is unavailable (no secret service): the GitHub sign-in cannot be kept.",
            localizer.Get("update.keychain_unavailable", Language.En).Replace("{reason}", check.KeychainReason, StringComparison.Ordinal));
        Assert.Contains("({reason})", localizer.Get("update.keychain_unavailable", Language.Fr), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeychainThatFailsWhenTheTokenArrivesEndsTheSignInFailedWithNothingStored()
    {
        var keychain = new ThrowingKeychain();
        var time = new FakeTimeProvider();
        var github = new FakeGitHub()
            .On("POST", Login + "device/code", HttpStatusCode.OK, Fixture("device-code.json"))
            .On("POST", Login + "oauth/access_token", HttpStatusCode.OK, Fixture("token.json"))
            .On("GET", "https://api.github.com/user", HttpStatusCode.OK, Fixture("user.json"));
        using var service = Create(github, keychain, time);

        await service.StartSignInAsync(userInitiated: true, Ct);
        await WaitForAsync(time, () => service.GetSignInState().Phase == SignInPhase.Failed);

        Assert.Contains("no secret service", service.GetSignInState().Reason, StringComparison.Ordinal);
    }

    private static UpdateService Create(FakeGitHub github, IKeychain keychain, TimeProvider? time = null)
    {
        var config = new LibraryConfig(
            AppConfig.Repo, AppConfig.TagPrefix, "d3skcommander/0.2.0", ReleaseKey.PublicKeyPem, ReleaseKey.Fingerprint,
            AppConfig.ClientIdEnvName, AppConfig.KeychainService, version => "d3skcommander-" + version + "-windows-x86_64.zip");
        return new UpdateService(new JobService(), config, github.Client(), keychain, setting: _ => null, time: time);
    }

    private static MemoryKeychain Stale()
    {
        var keychain = new MemoryKeychain();
        keychain.Set(AccessEntry, StaleToken);
        keychain.Set(RefreshEntry, StaleRefresh);
        return keychain;
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "github-oauth", "2022-11-28", name));

    // The poll timer fires when the fake clock reaches it: move on in steps until the condition holds.
    private static async Task WaitForAsync(FakeTimeProvider time, Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!done())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("the sign-in did not reach the expected state");
            }

            time.Advance(TimeSpan.FromSeconds(6));
            await Task.Delay(20, Ct);
        }
    }

    private sealed class ThrowingKeychain : IKeychain
    {
        private int _writes;

        public int Writes => _writes;

        public string? Get(string name) => throw new KeychainException("no secret service");

        public void Set(string name, string secret)
        {
            Interlocked.Increment(ref _writes);
            throw new KeychainException("no secret service");
        }

        public bool Remove(string name) => throw new KeychainException("no secret service");
    }
}
