namespace D3skCommander.Core.Timestamps;

/// <summary>AddTime (<see cref="ShiftTimes"/>), ReDate and touch (<see cref="SetTimes"/>), from v0.1.</summary>
public static partial class FileTimes
{
    /// <summary>
    /// AddTime: move the modification and/or access time of each file by <paramref name="offsetSeconds"/>
    /// (negative is earlier).
    /// </summary>
    public static List<FileTimeResult> ShiftTimes(
        IReadOnlyList<string> files,
        long offsetSeconds,
        bool modified,
        bool accessed)
    {
        ArgumentNullException.ThrowIfNull(files);
        var results = new List<FileTimeResult>(files.Count);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            results.Add(ShiftOne(file, offsetSeconds, modified, accessed));
        }

        return results;
    }

    /// <summary>
    /// ReDate: the file at index i gets modification time <paramref name="start"/> plus i times
    /// <paramref name="stepSeconds"/>.
    /// </summary>
    public static List<FileTimeResult> ReDate(
        IReadOnlyList<string> files,
        DateTimeOffset start,
        long stepSeconds)
    {
        ArgumentNullException.ThrowIfNull(files);
        var results = new List<FileTimeResult>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            ArgumentNullException.ThrowIfNull(file);
            results.Add(ReDateOne(file, start, i, stepSeconds));
        }

        return results;
    }

    /// <summary>Touch: set the modification and/or access time. A null time keeps the current one.</summary>
    public static List<FileTimeResult> SetTimes(
        IReadOnlyList<string> files,
        DateTimeOffset? modified,
        DateTimeOffset? accessed)
    {
        ArgumentNullException.ThrowIfNull(files);
        var results = new List<FileTimeResult>(files.Count);
        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            results.Add(SetOne(file, modified, accessed));
        }

        return results;
    }

    private static FileTimeResult ShiftOne(string path, long offsetSeconds, bool modified, bool accessed)
    {
        try
        {
            var mtime = ReadModified(path);
            var atime = ReadAccessed(path, mtime);
            if (modified && !TryShift(mtime, offsetSeconds, out mtime, out var modifiedError))
            {
                return FileTimeResult.Fail(path, modifiedError);
            }

            if (accessed && !TryShift(atime, offsetSeconds, out atime, out var accessedError))
            {
                return FileTimeResult.Fail(path, accessedError);
            }

            Apply(path, modified ? mtime : null, accessed ? atime : null);
            return FileTimeResult.Ok(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return FileTimeResult.FromException(path, ex);
        }
    }

    private static FileTimeResult ReDateOne(string path, DateTimeOffset start, int index, long stepSeconds)
    {
        if (!TryStep(index, stepSeconds, out var offset, out var stepError))
        {
            return FileTimeResult.Fail(path, stepError);
        }

        if (!TryShift(start, offset, out var time, out var shiftError))
        {
            return FileTimeResult.Fail(path, shiftError);
        }

        return SetOne(path, time, null);
    }

    private static FileTimeResult SetOne(string path, DateTimeOffset? modified, DateTimeOffset? accessed)
    {
        if (modified is null && accessed is null)
        {
            return FileTimeResult.Ok(path);
        }

        try
        {
            Apply(path, modified, accessed);
            return FileTimeResult.Ok(path);
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return FileTimeResult.FromException(path, ex);
        }
    }

    private static void Apply(string path, DateTimeOffset? modified, DateTimeOffset? accessed)
    {
        if (modified is DateTimeOffset mtime)
        {
            File.SetLastWriteTimeUtc(path, mtime.UtcDateTime);
        }

        if (accessed is DateTimeOffset atime)
        {
            File.SetLastAccessTimeUtc(path, atime.UtcDateTime);
        }
    }

    private static DateTimeOffset ReadModified(string path) => FromUtc(File.GetLastWriteTimeUtc(path));

    // v0.1 uses the modification time when the file system has no access time.
    private static DateTimeOffset ReadAccessed(string path, DateTimeOffset modified)
    {
        try
        {
            return FromUtc(File.GetLastAccessTimeUtc(path));
        }
        catch (Exception ex) when (IsFileSystem(ex))
        {
            return modified;
        }
    }

    private static DateTimeOffset FromUtc(DateTime utc) =>
        new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private static bool TryShift(DateTimeOffset time, long seconds, out DateTimeOffset shifted, out string error)
    {
        try
        {
            shifted = new DateTimeOffset(
                checked(time.Ticks + (seconds * TimeSpan.TicksPerSecond)),
                time.Offset);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            shifted = default;
            error = "resulting time is out of range";
            return false;
        }
    }

    private static bool TryStep(int index, long stepSeconds, out long offset, out string error)
    {
        try
        {
            offset = checked((long)index * stepSeconds);
            error = "";
            return true;
        }
        catch (OverflowException)
        {
            offset = 0;
            error = "step is out of range";
            return false;
        }
    }

    private static bool IsFileSystem(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
