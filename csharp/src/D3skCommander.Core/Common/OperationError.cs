using System.Net.Sockets;

namespace D3skCommander.Core.Common;

/// <summary>
/// A typed failure. <see cref="Message"/> keeps v0.1's English detail (or the OS text);
/// <see cref="Kind"/> is what the UI translates. <see cref="OsError"/> is the HRESULT when
/// the failure came from an exception.
/// </summary>
public sealed record OperationError(ErrorKind Kind, string Message, string? Path = null, int? OsError = null)
{
    /// <summary>v0.1 <c>ErrorKind::NotFound</c>. The message stays <c>not found</c> unless one is given.</summary>
    public static OperationError NotFound(string path, string? message = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new OperationError(ErrorKind.NotFound, Text(message, "not found"), path);
    }

    /// <summary>v0.1 <c>ErrorKind::PermissionDenied</c> (access denied, wrong password).</summary>
    public static OperationError AccessDenied(string message, string? path = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        return new OperationError(ErrorKind.AccessDenied, message, path);
    }

    /// <summary>v0.1 <c>ErrorKind::AlreadyExists</c>.</summary>
    public static OperationError AlreadyExists(string path, string? message = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new OperationError(ErrorKind.AlreadyExists, Text(message, "already exists"), path);
    }

    /// <summary>v0.1 <c>ErrorKind::Interrupted</c> with the message <c>cancelled</c>.</summary>
    public static OperationError Cancelled(string? path = null) =>
        new(ErrorKind.Cancelled, "cancelled", path);

    /// <summary>v0.1 <c>ErrorKind::InvalidData</c> and <c>InvalidInput</c>.</summary>
    public static OperationError InvalidData(string message, string? path = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        return new OperationError(ErrorKind.InvalidData, message, path);
    }

    /// <summary>v0.1 <c>ErrorKind::Unsupported</c>.</summary>
    public static OperationError Unsupported(string message, string? path = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        return new OperationError(ErrorKind.Unsupported, message, path);
    }

    public static OperationError Network(string message, string? path = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        return new OperationError(ErrorKind.Network, message, path);
    }

    public static OperationError Other(string message, string? path = null, int? osError = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        return new OperationError(ErrorKind.Other, message, path, osError);
    }

    /// <summary>
    /// Classifies v0.1's English <c>Result&lt;_, String&gt;</c> text. First matching phrase wins,
    /// so "not supported" is seen before any network phrase ("network drives are not supported"
    /// is unsupported, not a network failure). An unrecognized message is <see cref="ErrorKind.Other"/>.
    /// </summary>
    public static OperationError FromMessage(string message, string? path = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        return new OperationError(KindFromMessage(message) ?? ErrorKind.Other, message, path);
    }

    /// <summary>Maps a thrown failure onto the same kinds. The inner exception is used when the outer one names no kind.</summary>
    public static OperationError FromException(Exception exception, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var kind = Classify(current);
            if (kind is null)
            {
                continue;
            }

            var found = PathOf(current) ?? path;
            return new OperationError(kind.Value, Detail(current), found, current.HResult);
        }

        return new OperationError(ErrorKind.Other, Detail(exception), path, exception.HResult);
    }

    private static string Text(string? message, string fallback) =>
        string.IsNullOrEmpty(message) ? fallback : message;

    private static string Detail(Exception exception) =>
        string.IsNullOrEmpty(exception.Message) ? exception.GetType().Name : exception.Message;

    private static string? PathOf(Exception exception)
    {
        if (exception is FileNotFoundException missing && !string.IsNullOrEmpty(missing.FileName))
        {
            return missing.FileName;
        }

        return null;
    }

    private static ErrorKind? Classify(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return ErrorKind.Cancelled;
        }

        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return ErrorKind.NotFound;
        }

        if (exception is UnauthorizedAccessException)
        {
            return ErrorKind.AccessDenied;
        }

        if (exception is SocketException or HttpRequestException)
        {
            return ErrorKind.Network;
        }

        // EndOfStreamException is an IOException, so it has to be named before that case.
        if (exception is InvalidDataException or EndOfStreamException or FormatException)
        {
            return ErrorKind.InvalidData;
        }

        if (exception is NotSupportedException)
        {
            return ErrorKind.Unsupported;
        }

        if (exception is IOException io)
        {
            return KindFromIo(io);
        }

        return KindFromMessage(exception.Message);
    }

    // Win32 codes .NET stores in the low 16 bits of an IOException HRESULT.
    // 2/3 not found, 5/32/33 access or sharing, 80/183 already exists, 50 unsupported, 1392/1393 corrupt.
    private static ErrorKind? KindFromIo(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        if (code is 2 or 3)
        {
            return ErrorKind.NotFound;
        }

        if (code is 5 or 32 or 33)
        {
            return ErrorKind.AccessDenied;
        }

        if (code is 80 or 183)
        {
            return ErrorKind.AlreadyExists;
        }

        if (code is 50)
        {
            return ErrorKind.Unsupported;
        }

        if (code is 1392 or 1393)
        {
            return ErrorKind.InvalidData;
        }

        if (IsNetworkCode(code))
        {
            return ErrorKind.Network;
        }

        return KindFromMessage(exception.Message);
    }

    // ERROR_BAD_NETPATH, NETNAME_DELETED, BAD_NET_NAME, NO_NETWORK, CONNECTION_REFUSED,
    // NETWORK_UNREACHABLE, HOST_UNREACHABLE, CONNECTION_ABORTED, and the WSA equivalents.
    private static bool IsNetworkCode(int code) => code is
        53 or 64 or 67 or 1222 or 1225 or 1231 or 1232 or 1236
        or 10054 or 10060 or 10061 or 10065;

    private static ErrorKind? KindFromMessage(string message)
    {
        if (IsCancelled(message))
        {
            return ErrorKind.Cancelled;
        }

        foreach (var (phrase, kind) in Phrases)
        {
            if (message.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }

    // v0.1's cancelled() is the whole message, or the part after the last colon when a path was prefixed.
    private static bool IsCancelled(string message)
    {
        if (message.Equals("cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var colon = message.LastIndexOf(':');
        return colon >= 0
            && message[(colon + 1)..].Trim().Equals("cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly (string Phrase, ErrorKind Kind)[] Phrases =
    [
        ("already exists", ErrorKind.AlreadyExists),
        ("not found", ErrorKind.NotFound),
        ("wrong password", ErrorKind.AccessDenied),
        ("password protected", ErrorKind.AccessDenied),
        ("administrator rights refused", ErrorKind.AccessDenied),
        ("access denied", ErrorKind.AccessDenied),
        ("permission denied", ErrorKind.AccessDenied),
        ("not supported", ErrorKind.Unsupported),
        ("unsupported", ErrorKind.Unsupported),
        ("unknown archive type", ErrorKind.Unsupported),
        ("cannot be created", ErrorKind.Unsupported),
        ("external tool", ErrorKind.Unsupported),
        ("symlinks are not copied", ErrorKind.Unsupported),
        ("built from source", ErrorKind.Unsupported),
        ("download failed", ErrorKind.Network),
        ("upload failed", ErrorKind.Network),
        ("connection refused", ErrorKind.Network),
        ("network unreachable", ErrorKind.Network),
        ("timed out", ErrorKind.Network),
        ("timeout", ErrorKind.Network),
        ("not a valid", ErrorKind.InvalidData),
        ("not a directory", ErrorKind.InvalidData),
        ("not a drive letter", ErrorKind.InvalidData),
        ("not a final release", ErrorKind.InvalidData),
        ("invalid", ErrorKind.InvalidData),
        ("corrupt", ErrorKind.InvalidData),
        ("unsafe path", ErrorKind.InvalidData),
        ("no host given", ErrorKind.InvalidData),
        ("no file name", ErrorKind.InvalidData),
        ("nothing to launch", ErrorKind.InvalidData),
        ("empty command line", ErrorKind.InvalidData),
        ("empty catalog", ErrorKind.InvalidData),
        ("empty time offset", ErrorKind.InvalidData),
        ("missing server name", ErrorKind.InvalidData),
        ("missing share name", ErrorKind.InvalidData),
        ("out of range", ErrorKind.InvalidData),
        ("no fields", ErrorKind.InvalidData),
        ("part size must be greater than 0", ErrorKind.InvalidData),
        ("output would overwrite a part", ErrorKind.InvalidData),
        ("size too large", ErrorKind.InvalidData),
        ("cannot tell the checksum algorithm", ErrorKind.InvalidData),
    ];
}
