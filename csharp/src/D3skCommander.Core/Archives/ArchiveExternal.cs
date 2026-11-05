using D3skCommander.Core.Common;

namespace D3skCommander.Core.Archives;

/// <summary>RAR and the other formats. Row 26 replaces these stubs with the external tool.</summary>
internal static class ArchiveExternal
{
    public static ArchiveResult<IReadOnlyList<ArchiveEntry>> List(string path) =>
        ArchiveResult<IReadOnlyList<ArchiveEntry>>.Fail(OperationError.Unsupported("an external tool is required", path));

    public static ArchiveResult<int> Extract(string path) =>
        ArchiveResult<int>.Fail(OperationError.Unsupported("an external tool is required", path));
}
