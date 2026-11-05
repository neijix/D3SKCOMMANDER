namespace D3skCommander.Core.Compare;

/// <summary>One side of the compare editor. The text is LF-only; <see cref="Ending"/> is what a save writes.</summary>
public sealed class CompareBuffer
{
    /// <summary>Files above this size are not loaded into the compare editor.</summary>
    internal const long MaxBytes = 4 * 1024 * 1024;

    private CompareBuffer(string path, LineEndings.Decoded decoded)
    {
        Path = path;
        Text = decoded.Text;
        Ending = decoded.Ending;
        Bom = decoded.Bom;
    }

    public string Path { get; }

    public string Text { get; private set; }

    public LineEnding Ending { get; }

    public bool Bom { get; }

    public bool Dirty { get; private set; }

    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
        Dirty = true;
    }

    internal byte[] Encode() => LineEndings.Encode(Text, Ending, Bom);

    internal void MarkClean() => Dirty = false;

    internal static LoadResult Load(string path)
    {
        try
        {
            return LoadExisting(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return LoadResult.Fail(CompareStatus.Error($"{path}: {ex.Message}"));
        }
    }

    private static LoadResult LoadExisting(string path)
    {
        if (new FileInfo(path).Length > MaxBytes)
        {
            return LoadResult.Fail(CompareStatus.TooLarge(path));
        }

        var decoded = LineEndings.Decode(File.ReadAllBytes(path));
        return LoadResult.Ok(new CompareBuffer(path, decoded));
    }

    internal readonly struct LoadResult
    {
        private LoadResult(CompareBuffer? buffer, CompareStatus status)
        {
            Buffer = buffer;
            Status = status;
        }

        public CompareBuffer? Buffer { get; }

        public CompareStatus Status { get; }

        public static LoadResult Ok(CompareBuffer buffer) => new(buffer, CompareStatus.Empty);

        public static LoadResult Fail(CompareStatus status) => new(null, status);
    }
}
