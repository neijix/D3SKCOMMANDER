using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using D3sk.Update.SignIn;
using D3sk.Update.UpdateClient;
using D3skCommander.App.Host;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Update;

namespace D3skCommander.App.Features.Update.SignIn;

/// <summary>
/// The sign-in part of the Update window: a button after a check that met a private repository, then the code to type on
/// the device page, then the result. Only its button starts the device flow.
/// </summary>
public sealed partial class SignInPanel : ObservableObject, IDisposable
{
    private readonly SignInFlow _flow;
    private readonly ILocalizer _text;
    private readonly Func<SignInState, string> _failureLine;
    private readonly Func<Task> _recheck;
    private bool _needed;

    [ObservableProperty]
    private bool _isOffered;

    [ObservableProperty]
    private string _line = "";

    public SignInPanel(SignInFlow flow, ILocalizer text, Func<SignInState, string> failureLine, Func<Task> recheck)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(failureLine);
        ArgumentNullException.ThrowIfNull(recheck);
        _flow = flow;
        _text = text;
        _failureLine = failureLine;
        _recheck = recheck;
        _flow.Changed += state => UiThread.Post(() => Show(state));
    }

    /// <summary>The sign-in in progress, for the window to close with.</summary>
    public SignInFlow Flow => _flow;

    public string ButtonText => _text.Get("update.sign_in");

    /// <summary>Follows a finished check: the button shows while the check says a sign-in is needed.</summary>
    public void Observe(UpdateCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _needed = check.Status == CheckStatus.SignInNeeded;
        IsOffered = _needed;
        if (_needed)
        {
            Line = "";
        }
    }

    [RelayCommand]
    public async Task SignInAsync()
    {
        IsOffered = false;
        Line = "";
        await _flow.StartAsync().ConfigureAwait(true);
    }

    /// <summary>The window closed: cancels the sign-in and its job.</summary>
    public void Dispose() => _flow.Close();

    private void Show(SignInState state)
    {
        switch (state.Phase)
        {
            case SignInPhase.Waiting:
                Line = _text.Get("update.sign_in_code")
                    .Replace("{code}", state.UserCode ?? "", StringComparison.Ordinal)
                    .Replace("{url}", SignInPolicy.VerificationUrl, StringComparison.Ordinal);
                break;
            case SignInPhase.SignedIn:
                Line = "";
                _ = _recheck();
                break;
            case SignInPhase.Failed:
                Line = _failureLine(state);
                IsOffered = _needed;
                break;
            default:
                Line = "";
                IsOffered = _needed;
                break;
        }
    }
}
