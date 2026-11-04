namespace D3skCommander.Core.Common;

/// <summary>
/// What v0.1 reported, as a kind instead of an English <c>Result&lt;_, String&gt;</c>
/// or an <c>io::ErrorKind</c>. <see cref="Other"/> is v0.1's <c>ErrorKind::Other</c>
/// and any message that names none of the kinds below.
/// </summary>
public enum ErrorKind
{
    NotFound,
    AccessDenied,
    AlreadyExists,
    Cancelled,
    InvalidData,
    Unsupported,
    Network,
    Other,
}

/// <summary>Catalog key for a kind. The UI localizes it; the error itself stays untranslated.</summary>
public static class ErrorText
{
    public static string Key(ErrorKind kind) => kind switch
    {
        ErrorKind.NotFound => "error.not_found",
        ErrorKind.AccessDenied => "error.access_denied",
        ErrorKind.AlreadyExists => "error.already_exists",
        ErrorKind.Cancelled => "error.cancelled",
        ErrorKind.InvalidData => "error.invalid_data",
        ErrorKind.Unsupported => "error.unsupported",
        ErrorKind.Network => "error.network",
        ErrorKind.Other => "error.other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No text key for this error kind."),
    };
}
