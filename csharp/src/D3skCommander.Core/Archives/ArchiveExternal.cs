using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

/// <summary>RAR and the other formats through an installed <c>7z</c> / <c>unrar</c>.</summary>
public static partial class ArchiveExternal
{
    private const string MissingToolMessage =
        "this archive format needs an external tool: install 7-Zip (7z) or unrar and make sure it is on PATH";

    private static readonly string[] ToolNames = ["7z", "7za", "7zz", "unrar"];

    /// <summary>First of <c>7z</c>, <c>7za</c>, <c>7zz</c>, <c>unrar</c> on <c>PATH</c>, or null.</summary>
    public static string? FindTool() => FindOnPath(Environment.GetEnvironmentVariable("PATH"));

    /// <summary>Same search as <see cref="FindTool"/> over an explicit PATH string.</summary>
    public static string? FindOnPath(string? pathEnv)
    {
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var name in ToolNames)
        {
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var plain = Path.Combine(dir, name);
                if (File.Exists(plain))
                {
                    return plain;
                }

                var exe = plain + ".exe";
                if (File.Exists(exe))
                {
                    return exe;
                }
            }
        }

        return null;
    }

    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List(string path) =>
        List(path, FindTool(), Run);

    /// <summary>
    /// Lists <paramref name="path"/> with <paramref name="tool"/>. A null tool is unsupported.
    /// <paramref name="run"/> is the process (tests pass a fake). <c>unrar</c> is parsed as <c>lb</c>;
    /// anything else as <c>7z l -slt</c>.
    /// </summary>
    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List(
        string path,
        string? tool,
        Func<string, IReadOnlyList<string>, ExternalToolOutput> run)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(run);
        if (string.IsNullOrEmpty(tool))
        {
            return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Fail(Missing(path));
        }

        var arguments = new List<string>();
        if (IsUnrar(tool))
        {
            arguments.Add("lb");
            arguments.Add("-p-");
        }
        else
        {
            arguments.Add("l");
            arguments.Add("-slt");
        }

        arguments.Add("--");
        arguments.Add(path);
        var read = ReadOutput(tool, run(tool, arguments));
        if (read.Error is not null)
        {
            return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Fail(read.Error);
        }

        var parsed = IsUnrar(tool) ? ParseLb(read.Value) : ParseSlt(read.Value);
        var entries = new List<ArchiveEntry>();
        entries.AddRange(parsed);
        return ArchiveResult<IReadOnlyList<ArchiveEntry>>.Ok(ArchivePaths.WithImpliedDirectories(entries));
    }

    public static ArchiveResult<int> Extract(
        string archive,
        IReadOnlyList<string>? entries,
        string destination,
        Func<string, bool>? progress,
        CancellationToken cancellationToken) =>
        Extract(archive, entries, destination, progress, cancellationToken, FindTool(), Run);

    /// <summary>
    /// Extracts through <paramref name="tool"/>. Unsafe member names that the selection would write
    /// are refused before the tool runs. Returning false from <paramref name="progress"/> writes
    /// nothing and returns zero. A null tool is unsupported.
    /// </summary>
    public static ArchiveResult<int> Extract(
        string archive,
        IReadOnlyList<string>? entries,
        string destination,
        Func<string, bool>? progress,
        CancellationToken cancellationToken,
        string? tool,
        Func<string, IReadOnlyList<string>, ExternalToolOutput> run)
    {
        ArgumentException.ThrowIfNullOrEmpty(archive);
        ArgumentException.ThrowIfNullOrEmpty(destination);
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(tool))
        {
            return ArchiveResult<int>.Fail(Missing(archive));
        }

        var listed = List(archive, tool, run);
        if (listed.Error is not null)
        {
            return ArchiveResult<int>.Fail(listed.Error);
        }

        var refused = UnsafeMembers(listed.Value, entries);
        if (refused is not null)
        {
            return ArchiveResult<int>.Fail(refused);
        }

        var plan = ArchivePlan.From(entries);
        var count = FileCount(listed.Value, plan);
        if (progress is not null && !progress(Path.GetFileName(archive)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ArchiveResult<int>.Ok(0);
        }

        var created = CreateDirectory(destination);
        if (created is not null)
        {
            return ArchiveResult<int>.Fail(created);
        }

        if (entries is null)
        {
            var whole = ExtractArguments(tool, archive, destination, null);
            return RunExtract(tool, run, whole, count);
        }

        return ExtractSelection(tool, run, archive, destination, plan, Normalized(entries), count);
    }

    /// <summary>
    /// Why a member the selection would write escapes the destination, or null when every such
    /// member is safe. Paths are the normalized names <see cref="ParseSlt"/> stores.
    /// </summary>
    public static OperationError? UnsafeMembers(IReadOnlyList<ArchiveEntry> entries, IReadOnlyList<string>? selection)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var plan = ArchivePlan.From(selection);
        foreach (var entry in entries)
        {
            if (!plan.Wants(entry.Path))
            {
                continue;
            }

            var reason = ArchivePaths.UnsafeReason(entry.Path);
            if (reason is not null)
            {
                return OperationError.InvalidData($"unsafe path in archive ({reason}): {entry.Path}");
            }
        }

        return null;
    }

    private static int FileCount(IReadOnlyList<ArchiveEntry> entries, ArchivePlan plan)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (!entry.IsDirectory && plan.Wants(entry.Path))
            {
                count++;
            }
        }

        return count;
    }

    private static string[] Normalized(IReadOnlyList<string> entries)
    {
        var names = new string[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            names[i] = ArchivePaths.Normalize(entries[i] ?? "");
        }

        return names;
    }

    private static OperationError Missing(string path) =>
        OperationError.Unsupported(MissingToolMessage, path);

    private static OperationError? CreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ArchiveErrors.From(ex, path);
        }
    }

    private static ArchiveResult<string> ReadOutput(string tool, ExternalToolOutput output)
    {
        if (!string.IsNullOrEmpty(output.StartError))
        {
            return ArchiveResult<string>.Fail(OperationError.Other("cannot run " + tool + ": " + output.StartError));
        }

        if (output.ExitCode == 0)
        {
            return ArchiveResult<string>.Ok(output.StandardOutput);
        }

        var message = output.StandardError.Trim();
        if (message.Length == 0)
        {
            message = output.StandardOutput.Trim();
        }

        if (message.Length == 0)
        {
            message = "unknown error";
        }

        var text = tool + " failed (exit code " + output.ExitCode + "): " + LastLine(message);
        return ArchiveResult<string>.Fail(OperationError.Other(text));
    }

    private static string LastLine(string message)
    {
        var end = message.Length;
        while (end > 0 && (message[end - 1] == '\n' || message[end - 1] == '\r'))
        {
            end--;
        }

        var start = 0;
        for (var i = 0; i < end; i++)
        {
            if (message[i] == '\n')
            {
                start = i + 1;
            }
        }

        return message[start..end];
    }
}
