using System.Globalization;
using System.Text;

namespace D3skCommander.Core.OperationLog;

/// <summary>
/// Appends one line per operation when <see cref="Enabled"/> is on.
/// The sink is <c>ops.log</c> in the settings folder once one is given,
/// and until then an in-memory list echoed to stderr.
/// </summary>
public sealed class OperationLog : IOperationLog
{
    /// <summary>File name inside the settings folder (not v0.1's <c>operations.log</c>).</summary>
    public const string FileName = "ops.log";

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeProvider _time;
    private readonly TextWriter _error;
    private readonly object _gate = new();
    private readonly List<string> _memory = [];
    private string? _settingsFolder;
    private bool _enabled;

    public OperationLog(TimeProvider? time = null, TextWriter? error = null)
    {
        _time = time ?? TimeProvider.System;
        _error = error ?? Console.Error;
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }

        set
        {
            lock (_gate)
            {
                _enabled = value;
            }
        }
    }

    public void Record(string operation, string source, string? destination, string? error)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            var line = FormatLine(operation, source, destination, error);
            if (_settingsFolder is null)
            {
                _memory.Add(line);
                Echo(line);
                return;
            }

            try
            {
                File.AppendAllText(LogPath(_settingsFolder), line + "\n", Utf8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // v0.1 log_line: a failed append never fails the operation.
            }
        }
    }

    public void UseSettingsFolder(string settingsFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFolder);
        var full = Path.GetFullPath(settingsFolder);
        Directory.CreateDirectory(full);
        lock (_gate)
        {
            if (_memory.Count > 0)
            {
                var pending = new StringBuilder();
                foreach (var line in _memory)
                {
                    pending.Append(line);
                    pending.Append('\n');
                }

                File.AppendAllText(LogPath(full), pending.ToString(), Utf8);
                _memory.Clear();
            }

            _settingsFolder = full;
        }
    }

    public IReadOnlyList<string> ReadLines()
    {
        lock (_gate)
        {
            if (_settingsFolder is null)
            {
                return _memory.ToArray();
            }

            var path = LogPath(_settingsFolder);
            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                return File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return [];
            }
        }
    }

    private void Echo(string line)
    {
        try
        {
            _error.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The echo is diagnostic. Losing it still leaves the in-memory line.
        }
    }

    private string FormatLine(string operation, string source, string? destination, string? error)
    {
        var stamp = _time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var outcome = error is null ? "OK" : "ERROR " + Sanitize(error);
        return string.Join('\t', stamp, operation, source, destination ?? "", outcome);
    }

    /// <summary>Tabs and newlines in the message would split the five fields. v0.1 replaces them with spaces.</summary>
    private static string Sanitize(string message) =>
        message.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static string LogPath(string settingsFolder) => Path.Combine(settingsFolder, FileName);
}
