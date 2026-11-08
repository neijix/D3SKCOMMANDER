using D3skCommander.App.Features.Update.SignIn;

namespace D3skCommander.App.Features.Update;

public sealed partial class UpdateViewModel
{
    /// <summary>The sign-in part of the window; null when none is wired (the window then offers no sign-in).</summary>
    public SignInPanel? SignInPanel { get; set; }
}
