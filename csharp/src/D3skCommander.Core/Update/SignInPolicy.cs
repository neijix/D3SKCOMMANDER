using D3sk.Update.SignIn;

namespace D3skCommander.Core.Update;

/// <summary>
/// What the app may do about the GitHub sign-in: only a person's own action starts the device flow (never the quiet
/// start-up check), and the one address the app may open is the device page.
/// </summary>
public static class SignInPolicy
{
    /// <summary>The only URL the app opens for the sign-in.</summary>
    public const string VerificationUrl = GitHubOAuth.VerificationUrl;

    /// <summary>Whether a device flow may start: only when the person asked (a menu item or a button), never on a quiet check.</summary>
    public static bool MayStartDeviceFlow(bool userInitiated) => userInitiated;

    /// <summary>Whether <paramref name="url"/> is exactly <see cref="VerificationUrl"/>: any other scheme, host, port, path or query is refused.</summary>
    public static bool MayOpen(string? url) => string.Equals(url, VerificationUrl, StringComparison.Ordinal);
}
