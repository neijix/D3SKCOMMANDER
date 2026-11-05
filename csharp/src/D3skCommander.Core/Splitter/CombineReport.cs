namespace D3skCommander.Core.Splitter;

/// <summary>
/// Result of joining <c>name.001</c>, <c>name.002</c>, ….
/// <see cref="CrcOk"/> is null when no readable <c>.crc</c> file sits next to the parts.
/// </summary>
public sealed class CombineReport
{
    public CombineReport(string output, int parts, bool? crcOk)
    {
        ArgumentException.ThrowIfNullOrEmpty(output);
        ArgumentOutOfRangeException.ThrowIfNegative(parts);
        Output = output;
        Parts = parts;
        CrcOk = crcOk;
    }

    public string Output { get; }

    public int Parts { get; }

    /// <summary>True when the sidecar matches, false on a mismatch, null when there is no sidecar.</summary>
    public bool? CrcOk { get; }
}
