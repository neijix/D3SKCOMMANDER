using D3skCommander.Core.Common;

namespace D3skCommander.Core.Localization;

/// <summary>
/// The user-facing line of a typed error: the localized text of its kind, then the path it is
/// about. The raw exception text (English, OS-specific) never reaches the user.
/// </summary>
public static class ErrorLines
{
    /// <summary>"Kind text: path", or the kind text alone when no path is known.</summary>
    public static string Describe(ILocalizer text, OperationError error, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(error);
        var kind = text.Get(ErrorText.Key(error.Kind));
        var where = error.Path ?? path;
        return string.IsNullOrEmpty(where) ? kind : kind + ": " + where;
    }

    /// <summary>The line of a thrown failure, classified by <see cref="OperationError.FromException"/>.</summary>
    public static string Describe(ILocalizer text, Exception exception, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Describe(text, OperationError.FromException(exception, path), path);
    }
}
