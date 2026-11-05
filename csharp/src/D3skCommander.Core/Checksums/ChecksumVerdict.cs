namespace D3skCommander.Core.Checksums;

/// <summary>What one line of a checksum list did. v0.1 <c>Verify</c>.</summary>
public enum ChecksumVerdictKind
{
    Ok,
    Mismatch,
    Missing,
    Error,
}

/// <summary>
/// One checked line. <see cref="RelativePath"/> is the path as written in the list.
/// <see cref="Detail"/> is set for <see cref="ChecksumVerdictKind.Error"/>.
/// </summary>
public sealed record ChecksumVerdict(string RelativePath, ChecksumVerdictKind Kind, string? Detail = null);
