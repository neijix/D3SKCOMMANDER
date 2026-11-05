using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;

namespace D3skCommander.App.Features.Crypt;

/// <summary>The user-facing line of an encryption failure. Never the raw exception text, never a secret.</summary>
internal static class CryptErrors
{
    public static string Describe(ILocalizer text, OperationError error)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(error);
        if (error.Kind == ErrorKind.AccessDenied && error.Message == "wrong password")
        {
            return text.Get("crypt.wrong_password");
        }

        return ErrorLines.Describe(text, error);
    }
}
