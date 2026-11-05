using System.Diagnostics;
using System.Text;
using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

public static partial class ArchiveExternal
{
    private static int _scratchSeq;

    /// <summary>Runs <paramref name="program"/> with <paramref name="arguments"/> and captures both streams.</summary>
    public static ExternalToolOutput Run(string program, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo
        {
            FileName = program,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return ExternalToolOutput.FailedToStart("failed to start");
            }

            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Task.WaitAll(stdout, stderr);
            process.WaitForExit();
            return new ExternalToolOutput(process.ExitCode, stdout.Result, stderr.Result, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex is AggregateException aggregate && aggregate.InnerException is not null
                ? aggregate.InnerException.Message
                : ex.Message;
            return ExternalToolOutput.FailedToStart(message);
        }
    }

    private static bool IsUnrar(string tool)
    {
        var stem = Path.GetFileNameWithoutExtension(tool);
        return stem.StartsWith("unrar", StringComparison.OrdinalIgnoreCase);
    }

    private static ArchiveResult<int> RunExtract(
        string tool,
        Func<string, IReadOnlyList<string>, ExternalToolOutput> run,
        IReadOnlyList<string> arguments,
        int count)
    {
        var read = ReadOutput(tool, run(tool, arguments));
        if (read.Error is not null)
        {
            return ArchiveResult<int>.Fail(read.Error);
        }

        return ArchiveResult<int>.Ok(count);
    }

    private static ArchiveResult<int> ExtractSelection(
        string tool,
        Func<string, IReadOnlyList<string>, ExternalToolOutput> run,
        string archive,
        string destination,
        ArchivePlan plan,
        IReadOnlyList<string> names,
        int count)
    {
        var scratch = ScratchDirectory(destination);
        var created = CreateDirectory(scratch);
        if (created is not null)
        {
            return ArchiveResult<int>.Fail(created);
        }

        try
        {
            var ran = RunExtract(tool, run, ExtractArguments(tool, archive, scratch, names), count);
            if (ran.Error is not null)
            {
                return ran;
            }

            var moved = Relocate(plan, names, scratch, destination);
            if (moved is not null)
            {
                return ArchiveResult<int>.Fail(moved);
            }

            return ran;
        }
        finally
        {
            DeleteTree(scratch);
        }
    }

    private static List<string> ExtractArguments(
        string tool,
        string archive,
        string destination,
        IReadOnlyList<string>? names)
    {
        var args = new List<string>();
        if (IsUnrar(tool))
        {
            args.Add("x");
            args.Add("-y");
            args.Add("-o+");
            args.Add("-p-");
            args.Add("--");
            args.Add(archive);
            if (names is not null)
            {
                foreach (var name in names)
                {
                    args.Add(name);
                    args.Add(name + "/*");
                }
            }

            args.Add(WithTrailingSeparator(destination));
            return args;
        }

        args.Add("x");
        args.Add("-y");
        args.Add("-o" + destination);
        args.Add("--");
        args.Add(archive);
        if (names is not null)
        {
            args.AddRange(names);
        }

        return args;
    }

    private static string WithTrailingSeparator(string destination)
    {
        if (destination.EndsWith(Path.DirectorySeparatorChar) || destination.EndsWith('/'))
        {
            return destination;
        }

        return destination + Path.DirectorySeparatorChar;
    }

    private static OperationError? Relocate(
        ArchivePlan plan,
        IReadOnlyList<string> selection,
        string scratch,
        string destination)
    {
        foreach (var name in selection)
        {
            var resolved = plan.Resolve(destination, name);
            if (resolved.Error is not null)
            {
                return resolved.Error;
            }

            if (resolved.Path is null)
            {
                continue;
            }

            var from = JoinMember(scratch, name);
            if (!NodeExists(from))
            {
                continue;
            }

            var error = MergeMove(from, resolved.Path);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private static OperationError? MergeMove(string from, string to)
    {
        try
        {
            if (Directory.Exists(from))
            {
                Directory.CreateDirectory(to);
                foreach (var child in Directory.GetFileSystemEntries(from))
                {
                    var error = MergeMove(child, Path.Combine(to, Path.GetFileName(child)));
                    if (error is not null)
                    {
                        return error;
                    }
                }

                return null;
            }

            var parent = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            MoveFile(from, to);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ArchiveErrors.From(ex, to);
        }
    }

    private static void MoveFile(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (IOException)
        {
            File.Copy(from, to, overwrite: true);
        }
    }

    private static string JoinMember(string root, string name)
    {
        var path = root;
        foreach (var part in name.Split('/'))
        {
            if (part.Length > 0)
            {
                path = Path.Combine(path, part);
            }
        }

        return path;
    }

    private static bool NodeExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ArchiveErrors.IsIo(ex))
        {
            return false;
        }
    }

    private static string ScratchDirectory(string destination)
    {
        var n = Interlocked.Increment(ref _scratchSeq);
        return Path.Combine(destination, ".d3skcmd-extract-" + Environment.ProcessId + "-" + n);
    }

    private static void DeleteTree(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ArchiveErrors.IsIo(ex))
        {
        }
    }
}
