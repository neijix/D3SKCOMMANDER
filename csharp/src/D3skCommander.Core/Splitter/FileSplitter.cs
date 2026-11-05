using System.IO.Hashing;
using D3skCommander.Core.Common;
using D3skCommander.Core.FileSystem;

namespace D3skCommander.Core.Splitter;

/// <summary>
/// Cuts a file into <c>name.001</c>, <c>name.002</c>, … plus <c>name.crc</c>, and joins those parts back.
/// A progress callback that returns false cancels and removes the partial output.
/// </summary>
public static class FileSplitter
{
    public static ParsedSize ParseSize(string text) => SplitSize.Parse(text);

    public static FileResult<IReadOnlyList<string>> Split(
        string source,
        string destinationDirectory,
        ulong partSize,
        Func<ulong, ulong, bool>? progress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        if (partSize == 0)
        {
            return FileResult<IReadOnlyList<string>>.Fail(
                OperationError.InvalidData("part size must be greater than 0", source));
        }

        var name = Path.GetFileName(source);
        if (string.IsNullOrEmpty(name))
        {
            return FileResult<IReadOnlyList<string>>.Fail(
                OperationError.InvalidData("source has no file name", source));
        }

        var parts = new List<string>();
        var keepGoing = progress ?? Continue;
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var written = WriteParts(source, destinationDirectory, name, partSize, parts, keepGoing);
            if (written.Error is not null)
            {
                SplitParts.RemoveAll(parts);
                return FileResult<IReadOnlyList<string>>.Fail(written.Error);
            }

            var crcError = WriteCrc(destinationDirectory, name, written.Size, written.Crc);
            if (crcError is not null)
            {
                SplitParts.RemoveAll(parts);
                return FileResult<IReadOnlyList<string>>.Fail(crcError);
            }

            return FileResult<IReadOnlyList<string>>.Ok(parts.ToArray());
        }
        catch (Exception exception) when (SplitParts.IsFileSystem(exception))
        {
            SplitParts.RemoveAll(parts);
            return FileResult<IReadOnlyList<string>>.Fail(OperationError.FromException(exception, source));
        }
    }

    public static FileResult<CombineReport> Combine(
        string anyPart,
        string destinationDirectory,
        Func<ulong, ulong, bool>? progress = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(anyPart);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        if (!SplitParts.TryBaseName(anyPart, out var baseName, out var nameError))
        {
            return FileResult<CombineReport>.Fail(nameError!);
        }

        var sourceDirectory = SplitParts.SourceDirectory(anyPart);
        try
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        catch (Exception exception) when (SplitParts.IsFileSystem(exception))
        {
            return FileResult<CombineReport>.Fail(
                OperationError.FromException(exception, destinationDirectory));
        }

        var output = Path.Combine(destinationDirectory, baseName);
        if (SamePath(SplitParts.PartPath(sourceDirectory, baseName, 1), output))
        {
            return FileResult<CombineReport>.Fail(
                OperationError.InvalidData("output would overwrite a part", output));
        }

        return Join(sourceDirectory, baseName, output, progress ?? Continue);
    }

    private static bool Continue(ulong done, ulong total) => true;

    private static PartWrite WriteParts(
        string source,
        string destinationDirectory,
        string name,
        ulong partSize,
        List<string> parts,
        Func<ulong, ulong, bool> progress)
    {
        using var input = OpenRead(source);
        var total = (ulong)input.Length;
        var crc = new Crc32();
        ulong done = 0;
        var number = 0;
        while (true)
        {
            number++;
            var path = SplitParts.PartPath(destinationDirectory, name, number);
            parts.Add(path);
            using var output = OpenWrite(path);
            var error = SplitParts.Copy(
                input, output, partSize, crc, ref done, total, progress, out var written);
            output.Flush();
            if (error is not null)
            {
                return PartWrite.Fail(error);
            }

            if (written < partSize || done >= total)
            {
                return PartWrite.Ok(total, crc.GetCurrentHashAsUInt32());
            }
        }
    }

    private static OperationError? WriteCrc(string directory, string name, ulong size, uint crc)
    {
        var path = Path.Combine(directory, name + ".crc");
        var text = $"filename={name}\nsize={size}\ncrc32={crc:X8}\n";
        try
        {
            File.WriteAllText(path, text);
            return null;
        }
        catch (Exception exception) when (SplitParts.IsFileSystem(exception))
        {
            return OperationError.FromException(exception, path);
        }
    }

    private static FileResult<CombineReport> Join(
        string sourceDirectory,
        string baseName,
        string output,
        Func<ulong, ulong, bool> progress)
    {
        PartAppend appended;
        try
        {
            using var stream = OpenWrite(output);
            appended = AppendParts(sourceDirectory, baseName, stream, progress);
            stream.Flush();
        }
        catch (Exception exception) when (SplitParts.IsFileSystem(exception))
        {
            return FailJoin(output, OperationError.FromException(exception, output));
        }

        if (appended.Error is not null)
        {
            return FailJoin(output, appended.Error);
        }

        var crcOk = SplitParts.CheckCrc(
            Path.Combine(sourceDirectory, baseName + ".crc"), appended.Crc, appended.Size);
        return FileResult<CombineReport>.Ok(new CombineReport(output, appended.Parts, crcOk));
    }

    private static FileResult<CombineReport> FailJoin(string output, OperationError error)
    {
        SplitParts.RemoveAll([output]);
        return FileResult<CombineReport>.Fail(error);
    }

    private static PartAppend AppendParts(
        string directory,
        string baseName,
        Stream output,
        Func<ulong, ulong, bool> progress)
    {
        var paths = ListParts(directory, baseName);
        if (paths.Count == 0)
        {
            var missing = SplitParts.PartPath(directory, baseName, 1);
            return PartAppend.Fail(OperationError.NotFound(missing, $"{baseName}.001 not found in {directory}"));
        }

        ulong total = 0;
        foreach (var path in paths)
        {
            total += (ulong)new FileInfo(path).Length;
        }

        var crc = new Crc32();
        ulong done = 0;
        foreach (var path in paths)
        {
            using var input = OpenRead(path);
            var error = SplitParts.Copy(input, output, ulong.MaxValue, crc, ref done, total, progress, out _);
            if (error is not null)
            {
                return PartAppend.Fail(error);
            }
        }

        return PartAppend.Ok(paths.Count, done, crc.GetCurrentHashAsUInt32());
    }

    private static List<string> ListParts(string directory, string baseName)
    {
        var paths = new List<string>();
        while (true)
        {
            var path = SplitParts.PartPath(directory, baseName, paths.Count + 1);
            if (!File.Exists(path))
            {
                return paths;
            }

            paths.Add(path);
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    private static FileStream OpenWrite(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.None);

    private readonly struct PartWrite
    {
        private PartWrite(ulong size, uint crc, OperationError? error)
        {
            Size = size;
            Crc = crc;
            Error = error;
        }

        public ulong Size { get; }

        public uint Crc { get; }

        public OperationError? Error { get; }

        public static PartWrite Ok(ulong size, uint crc) => new(size, crc, null);

        public static PartWrite Fail(OperationError error) => new(0, 0, error);
    }

    private readonly struct PartAppend
    {
        private PartAppend(int parts, ulong size, uint crc, OperationError? error)
        {
            Parts = parts;
            Size = size;
            Crc = crc;
            Error = error;
        }

        public int Parts { get; }

        public ulong Size { get; }

        public uint Crc { get; }

        public OperationError? Error { get; }

        public static PartAppend Ok(int parts, ulong size, uint crc) => new(parts, size, crc, null);

        public static PartAppend Fail(OperationError error) => new(0, 0, 0, error);
    }
}
