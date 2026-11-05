using System.Net.Sockets;
using D3skCommander.Core.Common;
using FluentFTP.Exceptions;

namespace D3skCommander.Core.Ftp;

/// <summary>Typed errors for what the FTP library throws.</summary>
internal static class FtpErrors
{
    public static OperationError CertificateRefused(string reason, string? path = null) =>
        OperationError.Network("server certificate refused: " + reason, path);

    public static OperationError Describe(Exception exception, string? path)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException)
        {
            return OperationError.Cancelled(path);
        }

        if (exception is FtpAuthenticationException)
        {
            return OperationError.AccessDenied("login refused: " + exception.Message, path);
        }

        if (exception is FtpCommandException command)
        {
            return FromReply(command.CompletionCode, command.Message, path);
        }

        if (exception is FtpException or IOException or SocketException or TimeoutException
            or System.Security.Authentication.AuthenticationException)
        {
            return OperationError.Network(exception.Message, path);
        }

        return OperationError.FromException(exception, path);
    }

    private static bool IsMissing(string lower) =>
        lower.Contains("not exist", StringComparison.Ordinal)
        || lower.Contains("n't exist", StringComparison.Ordinal)
        || lower.Contains("no such", StringComparison.Ordinal)
        || lower.Contains("not found", StringComparison.Ordinal);

    private static OperationError FromReply(string code, string message, string? path)
    {
        var lower = message.ToLowerInvariant();
        if (code.StartsWith('4') && lower.Contains("abort", StringComparison.Ordinal))
        {
            return OperationError.Cancelled(path);
        }

        if (code == "530" || code == "532" || lower.Contains("denied", StringComparison.Ordinal)
            || lower.Contains("permission", StringComparison.Ordinal))
        {
            return OperationError.AccessDenied(message, path);
        }

        if (lower.Contains("exist", StringComparison.Ordinal) && !IsMissing(lower))
        {
            return path is null ? OperationError.Other(message) : OperationError.AlreadyExists(path, message);
        }

        if (code == "550" || code == "450")
        {
            return path is null ? OperationError.Other(message) : OperationError.NotFound(path, message);
        }

        if (code.StartsWith('4'))
        {
            return OperationError.Network(message, path);
        }

        return OperationError.Other(message, path);
    }
}
