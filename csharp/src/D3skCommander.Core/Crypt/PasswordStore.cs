using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Crypt;

/// <summary>
/// Named secrets under a master password, in <see cref="FileName"/> (<c>.d3enc</c>).
/// A missing file opens as an empty store. Save writes atomically and, on Unix, mode 0600.
/// No Windows ACL is set. Secrets are not included in <see cref="ToString"/>.
/// </summary>
public sealed class PasswordStore
{
    public const string FileName = "vault.d3enc";

    private readonly string _path;
    private readonly string _master;
    private readonly Dictionary<string, string> _entries;

    private PasswordStore(string path, string master, Dictionary<string, string> entries)
    {
        _path = path;
        _master = master;
        _entries = entries;
    }

    /// <summary>
    /// Unlocks the store at <paramref name="path"/>. A missing file is an empty store.
    /// A wrong master password is <see cref="ErrorKind.AccessDenied"/>. A file that is not
    /// ours, or one whose plaintext is not the store document, is <see cref="ErrorKind.InvalidData"/>.
    /// </summary>
    public static FileResult<PasswordStore> Open(string path, string master)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(master);
        Stream? input;
        try
        {
            input = File.OpenRead(path);
        }
        catch (FileNotFoundException)
        {
            return FileResult<PasswordStore>.Ok(Empty(path, master));
        }
        catch (DirectoryNotFoundException)
        {
            return FileResult<PasswordStore>.Ok(Empty(path, master));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FileResult<PasswordStore>.Fail(OperationError.FromException(ex, path));
        }

        using (input)
        {
            return Read(path, master, input);
        }
    }

    public string? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(key, out var secret) ? secret : null;
    }

    public void Set(string key, string secret)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(secret);
        _entries[key] = secret;
    }

    public void Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _entries.Remove(key);
    }

    /// <summary>Stored names, in ordinal order.</summary>
    public IReadOnlyList<string> Keys()
    {
        var names = new List<string>(_entries.Count);
        foreach (var name in _entries.Keys)
        {
            names.Add(name);
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Encrypts the store with the master password and replaces the file atomically.
    /// A null cost uses <see cref="EncryptionCost.Interactive"/>.
    /// </summary>
    public FileStatus Save(EncryptionCost? cost = null)
    {
        var chosen = EncryptionCost.Resolve(cost);
        var json = Serialize();
        try
        {
            using var input = new MemoryStream(json, writable: false);
            return AtomicFile.Write(_path, ownerOnly: true, output =>
                SecretStream.Encrypt(input, output, _master, chosen, _ => true));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    public override string ToString() =>
        "PasswordStore { Path = " + _path + ", Entries = " + _entries.Count.ToString(CultureInfo.InvariantCulture) + " }";

    private static PasswordStore Empty(string path, string master) =>
        new(path, master, new Dictionary<string, string>(StringComparer.Ordinal));

    private static FileResult<PasswordStore> Read(string path, string master, Stream input)
    {
        using var plain = new MemoryStream();
        var status = SecretStream.Decrypt(input, plain, master, _ => true);
        if (!status.IsSuccess)
        {
            return FileResult<PasswordStore>.Fail(WithPath(status, path));
        }

        var bytes = plain.GetBuffer().AsSpan(0, (int)plain.Length);
        try
        {
            return Parse(path, master, bytes);
        }
        finally
        {
            bytes.Clear();
        }
    }

    private static FileResult<PasswordStore> Parse(string path, string master, ReadOnlySpan<byte> plain)
    {
        StoreDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<StoreDocument>(plain, StoreDocument.Options);
        }
        catch (JsonException)
        {
            return Corrupt(path);
        }

        if (document is null || document.Version != StoreDocument.CurrentVersion)
        {
            return Corrupt(path);
        }

        return FileResult<PasswordStore>.Ok(new PasswordStore(path, master, Copy(document.Entries)));
    }

    private static Dictionary<string, string> Copy(Dictionary<string, string>? entries)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entries is null)
        {
            return copy;
        }

        foreach (var pair in entries)
        {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }

    private byte[] Serialize()
    {
        var document = new StoreDocument
        {
            Version = StoreDocument.CurrentVersion,
            Entries = _entries,
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, StoreDocument.Options);
    }

    private static FileResult<PasswordStore> Corrupt(string path) =>
        FileResult<PasswordStore>.Fail(OperationError.InvalidData(CryptText.CorruptStore, path));

    private static OperationError WithPath(FileStatus status, string path)
    {
        var error = status.Error!;
        if (error.Path is not null)
        {
            return error;
        }

        return new OperationError(error.Kind, error.Message, path, error.OsError);
    }

    private sealed class StoreDocument
    {
        public const int CurrentVersion = 1;

        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        [JsonPropertyName("version")]
        public int Version { get; set; } = CurrentVersion;

        [JsonPropertyName("entries")]
        public Dictionary<string, string>? Entries { get; set; }
    }
}
