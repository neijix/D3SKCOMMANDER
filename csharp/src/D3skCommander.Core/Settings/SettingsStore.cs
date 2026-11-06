using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Settings;

/// <summary>
/// One JSON file in a settings folder: a schema version, an atomic replace, and a
/// <see cref="DebounceMilliseconds"/> debounce (the same interval as the quick filter).
/// A missing or partial file loads <c>new T()</c>. A truncated file is partial.
/// An unreadable file loads defaults, reports one <see cref="OperationError"/>, and is renamed
/// <c>&lt;name&gt;.corrupt-&lt;yyyyMMdd-HHmmss&gt;</c> before the first save.
/// The FTP site store uses this for its own file.
/// </summary>
public class SettingsStore<T> : IDisposable
    where T : class, new()
{
    /// <summary>Quiet period before a save hits disk. Matches the quick-filter debounce.</summary>
    public const int DebounceMilliseconds = 150;

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(DebounceMilliseconds);
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly object _write = new();
    private readonly string _folder;
    private readonly string _fileName;
    private readonly string _path;
    private readonly int _schema;
    private readonly TimeProvider _time;
    private readonly bool _unreadable;
    private string? _pending;
    private ITimer? _timer;
    private int _epoch;
    private OperationError? _error;
    private bool _preserved;

    public SettingsStore(string folder, string fileName, int schemaVersion, TimeProvider? time = null)
    {
        _folder = folder;
        _fileName = fileName;
        _path = FilePath(folder, fileName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        _schema = schemaVersion;
        _time = time ?? TimeProvider.System;
        Current = Read(out _unreadable);
    }

    public T Current { get; private set; }

    /// <summary>Raised on the saving thread each time <see cref="Save"/> stores a value, before it reaches the disk.</summary>
    public event Action<T>? Saved;

    /// <summary>The unreadable-file error, once. The next call returns null.</summary>
    public OperationError? TakeLoadError()
    {
        lock (_gate)
        {
            var error = _error;
            _error = null;
            return error;
        }
    }

    /// <summary>Remembers <paramref name="value"/> and writes it once the debounce window is quiet.</summary>
    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Current = value;
        Schedule(Serialize(value));
        Saved?.Invoke(value);
    }

    /// <summary>Writes a pending save immediately. <see cref="Dispose"/> does this too.</summary>
    public void Flush()
    {
        var json = TakePending(out var timer);
        timer?.Dispose();
        if (json is not null)
        {
            WriteAtomic(json);
        }
    }

    public void Dispose()
    {
        Flush();
    }

    private T Read(out bool unreadable)
    {
        var bytes = ReadPayload(out unreadable);
        if (bytes is null)
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bytes, SettingsJson.Options) ?? new T();
        }
        catch (JsonException)
        {
            unreadable = true;
            NoteUnreadable();
            return new T();
        }
    }

    private byte[]? ReadPayload(out bool unreadable)
    {
        unreadable = false;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable = true;
            NoteUnreadable();
            return null;
        }

        return Interpret(bytes, ref unreadable);
    }

    private byte[]? Interpret(byte[] bytes, ref bool unreadable)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            if (IsTruncated(bytes))
            {
                return null;
            }

            unreadable = true;
            NoteUnreadable();
            return null;
        }

        using (document)
        {
            if (Accepted(document))
            {
                return bytes;
            }
        }

        unreadable = true;
        NoteUnreadable();
        return null;
    }

    private bool Accepted(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty("schema", out var schema))
        {
            return true;
        }

        return schema.ValueKind == JsonValueKind.Number
            && schema.TryGetInt32(out var version)
            && version == _schema;
    }

    private void NoteUnreadable()
    {
        Remember(OperationError.InvalidData("unreadable settings file", _path));
        TryRename();
    }

    private void Remember(OperationError error)
    {
        lock (_gate)
        {
            _error ??= error;
        }
    }

    private string Serialize(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, SettingsJson.Options) as JsonObject ?? new JsonObject();
        node["schema"] = JsonValue.Create(_schema);
        return node.ToJsonString(SettingsJson.Options);
    }

    private void Schedule(string json)
    {
        var epoch = SwapTimer(json, out var previous);
        previous?.Dispose();
        var timer = _time.CreateTimer(OnTimer, epoch, Debounce, Timeout.InfiniteTimeSpan);
        KeepTimer(epoch, timer);
    }

    private int SwapTimer(string json, out ITimer? previous)
    {
        lock (_gate)
        {
            _pending = json;
            _epoch++;
            previous = _timer;
            _timer = null;
            return _epoch;
        }
    }

    private void KeepTimer(int epoch, ITimer timer)
    {
        lock (_gate)
        {
            if (epoch != _epoch)
            {
                timer.Dispose();
                return;
            }

            _timer = timer;
        }
    }

    private void OnTimer(object? state)
    {
        if (state is not int epoch)
        {
            return;
        }

        string? json;
        lock (_gate)
        {
            if (epoch != _epoch || _pending is null)
            {
                return;
            }

            json = _pending;
            _pending = null;
        }

        WriteAtomic(json);
    }

    private string? TakePending(out ITimer? timer)
    {
        lock (_gate)
        {
            var json = _pending;
            _pending = null;
            _epoch++;
            timer = _timer;
            _timer = null;
            return json;
        }
    }

    private void WriteAtomic(string json)
    {
        lock (_write)
        {
            try
            {
                Publish(json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Remember(OperationError.FromException(ex, _path));
            }
        }
    }

    private void Publish(string json)
    {
        if (_unreadable && !TryRename())
        {
            return;
        }

        Directory.CreateDirectory(_folder);
        var temp = Path.Combine(_folder, _fileName + ".tmp");
        try
        {
            File.WriteAllText(temp, json, Utf8);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            DeleteTemp(temp);
        }
    }

    private bool TryRename()
    {
        if (_preserved)
        {
            return true;
        }

        if (!File.Exists(_path))
        {
            _preserved = true;
            return true;
        }

        try
        {
            File.Move(_path, CorruptPath());
            _preserved = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string CorruptPath()
    {
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var stem = _path + ".corrupt-" + stamp;
        if (!File.Exists(stem))
        {
            return stem;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var numbered = stem + "-" + suffix.ToString(CultureInfo.InvariantCulture);
            if (!File.Exists(numbered))
            {
                return numbered;
            }
        }

        return stem + "-" + Guid.NewGuid().ToString("N");
    }

    private static void DeleteTemp(string temp)
    {
        if (!File.Exists(temp))
        {
            return;
        }

        try
        {
            File.Delete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsTruncated(ReadOnlySpan<byte> bytes)
    {
        if (IsBlank(bytes))
        {
            return true;
        }

        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        try
        {
            while (reader.Read())
            {
            }
        }
        catch (JsonException)
        {
            return reader.BytesConsumed >= bytes.Length;
        }

        return false;
    }

    private static bool IsBlank(ReadOnlySpan<byte> bytes)
    {
        foreach (var piece in bytes)
        {
            if (piece != (byte)' ' && piece != (byte)'\t' && piece != (byte)'\n' && piece != (byte)'\r')
            {
                return false;
            }
        }

        return true;
    }

    private static string FilePath(string folder, string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        if (fileName != Path.GetFileName(fileName))
        {
            throw new ArgumentException("The settings file name has to stay in the folder.", nameof(fileName));
        }

        return Path.Combine(folder, fileName);
    }
}
