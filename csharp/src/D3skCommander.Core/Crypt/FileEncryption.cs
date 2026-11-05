using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Crypt;

/// <summary>
/// Encrypts and decrypts a file as <c>.d3enc</c> (Argon2id and libsodium secretstream).
/// The destination is written through a temporary file, so cancel or a wrong password
/// leaves nothing behind. A file that is not ours is <see cref="ErrorKind.InvalidData"/>.
/// </summary>
public static class FileEncryption
{
    public const string Extension = ".d3enc";

    /// <summary><c>name.ext</c> becomes <c>name.ext.d3enc</c> in the same folder.</summary>
    public static string DefaultEncryptedPath(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        return InSameFolder(source, Path.GetFileName(source) + Extension);
    }

    /// <summary>
    /// Strips a trailing <c>.d3enc</c> (any case). A name without that suffix gets <c>.decrypted</c>.
    /// </summary>
    public static string DefaultDecryptedPath(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        var name = Path.GetFileName(source);
        if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^Extension.Length];
        }
        else
        {
            name += ".decrypted";
        }

        return InSameFolder(source, name);
    }

    /// <summary>
    /// Encrypts <paramref name="source"/> into <paramref name="destination"/>.
    /// <paramref name="progress"/> receives plaintext bytes read; returning false cancels.
    /// A null cost uses <see cref="EncryptionCost.Interactive"/>.
    /// </summary>
    public static FileStatus EncryptFile(
        string source,
        string destination,
        string password,
        Func<long, bool> progress,
        EncryptionCost? cost = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(progress);
        try
        {
            using var input = File.OpenRead(source);
            var status = AtomicFile.Write(destination, ownerOnly: false, output =>
                SecretStream.Encrypt(input, output, password, EncryptionCost.Resolve(cost), progress));
            return WithPath(status, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileStatus.Fail(OperationError.FromException(ex, source));
        }
    }

    /// <summary>
    /// Decrypts <paramref name="source"/> into <paramref name="destination"/>.
    /// A wrong password is <see cref="ErrorKind.AccessDenied"/> with the message
    /// <c>wrong password</c>. Nothing is left at the destination on failure.
    /// </summary>
    public static FileStatus DecryptFile(string source, string destination, string password, Func<long, bool> progress)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(progress);
        try
        {
            using var input = File.OpenRead(source);
            var status = AtomicFile.Write(destination, ownerOnly: false, output =>
                SecretStream.Decrypt(input, output, password, progress));
            return WithPath(status, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileStatus.Fail(OperationError.FromException(ex, source));
        }
    }

    private static string InSameFolder(string source, string name)
    {
        var directory = Path.GetDirectoryName(source);
        if (string.IsNullOrEmpty(directory))
        {
            return name;
        }

        return Path.Combine(directory, name);
    }

    private static FileStatus WithPath(FileStatus status, string path)
    {
        if (status.IsSuccess || status.Error!.Path is not null)
        {
            return status;
        }

        var error = status.Error;
        return FileStatus.Fail(new OperationError(error.Kind, error.Message, path, error.OsError));
    }
}
