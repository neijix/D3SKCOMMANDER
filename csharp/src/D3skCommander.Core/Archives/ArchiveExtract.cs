using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

internal readonly struct MemberView
{
    public MemberView(
        string raw,
        bool isDirectory,
        bool isLink,
        long size,
        long? compressedSize,
        DateTime? modified,
        uint? mode,
        Func<Stream>? open)
    {
        Raw = raw;
        IsDirectory = isDirectory;
        IsLink = isLink;
        Size = size;
        CompressedSize = compressedSize;
        Modified = modified;
        Mode = mode;
        Open = open;
    }

    public string Raw { get; }

    public bool IsDirectory { get; }

    public bool IsLink { get; }

    public long Size { get; }

    public long? CompressedSize { get; }

    public DateTime? Modified { get; }

    public uint? Mode { get; }

    public Func<Stream>? Open { get; }
}

internal static class ArchiveExtract
{
    public static ArchiveResult<int> Read(
        IEnumerable<MemberView> members,
        ArchivePlan plan,
        string destination,
        Func<string, bool>? progress,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.IsLink)
            {
                continue;
            }

            var step = Take(plan, destination, member, progress);
            if (step.Error is not null)
            {
                return ArchiveResult<int>.Fail(step.Error);
            }

            if (step.Stop)
            {
                StopIfCancelled(cancellationToken);
                break;
            }

            count += step.Files;
        }

        return ArchiveResult<int>.Ok(count);
    }

    public static void AddListed(List<ArchiveEntry> entries, MemberView member)
    {
        var name = ArchivePaths.Normalize(member.Raw);
        if (name.Length == 0)
        {
            return;
        }

        var isDirectory = member.IsDirectory && !member.IsLink;
        var size = member.IsLink || isDirectory ? 0 : Math.Max(0, member.Size);
        long? compressed = member.IsLink ? null : member.CompressedSize;
        entries.Add(new ArchiveEntry(name, size, compressed, isDirectory, AsOffset(member.Modified)));
    }

    private static Step Take(ArchivePlan plan, string destination, MemberView member, Func<string, bool>? progress)
    {
        var resolved = plan.Resolve(destination, member.Raw);
        if (resolved.Error is not null)
        {
            return Step.Failed(resolved.Error);
        }

        if (resolved.IsSkip || resolved.Path is null || resolved.Normalized is null)
        {
            return Step.Skipped();
        }

        if (progress is not null && !progress(resolved.Normalized))
        {
            return Step.Stopped();
        }

        if (member.IsDirectory || EndsWithSeparator(member.Raw))
        {
            Directory.CreateDirectory(resolved.Path);
            return Step.Directory();
        }

        WriteFile(resolved.Path, member);
        return Step.File();
    }

    private static void WriteFile(string path, MemberView member)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        RemoveLink(path);
        using (var output = File.Create(path))
        {
            if (member.Open is not null)
            {
                using var input = member.Open();
                input.CopyTo(output);
            }
        }

        TrySetTime(path, member.Modified);
        TrySetMode(path, member.Mode);
    }

    private static void RemoveLink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                info.Delete();
            }
        }
        catch (Exception ex) when (ArchiveErrors.IsIo(ex))
        {
        }
    }

    private static void TrySetTime(string path, DateTime? modified)
    {
        if (modified is null)
        {
            return;
        }

        try
        {
            var value = modified.Value;
            if (value.Kind == DateTimeKind.Utc)
            {
                File.SetLastWriteTimeUtc(path, value);
            }
            else
            {
                File.SetLastWriteTime(path, value);
            }
        }
        catch (Exception)
        {
            // v0.1 ignores a failure to set the date.
        }
    }

    private static void TrySetMode(string path, uint? mode)
    {
        if (mode is null || OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var bits = (mode.Value & 0x1FFu) | 0x180u;
            File.SetUnixFileMode(path, (UnixFileMode)bits);
        }
        catch (Exception)
        {
            // v0.1 ignores a failure to set the mode.
        }
    }

    private static void StopIfCancelled(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static bool EndsWithSeparator(string raw) =>
        raw.EndsWith('/') || raw.EndsWith('\\');

    private static DateTimeOffset? AsOffset(DateTime? time)
    {
        if (time is null)
        {
            return null;
        }

        var value = time.Value;
        if (value.Kind == DateTimeKind.Unspecified)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Local);
        }

        return new DateTimeOffset(value);
    }

    private readonly struct Step
    {
        private Step(int files, bool stop, OperationError? error)
        {
            Files = files;
            Stop = stop;
            Error = error;
        }

        public int Files { get; }

        public bool Stop { get; }

        public OperationError? Error { get; }

        public static Step File() => new(1, false, null);

        public static Step Directory() => new(0, false, null);

        public static Step Skipped() => new(0, false, null);

        public static Step Stopped() => new(0, true, null);

        public static Step Failed(OperationError error) => new(0, false, error);
    }
}
