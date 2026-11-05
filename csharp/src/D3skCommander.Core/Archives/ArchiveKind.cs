namespace D3skCommander.Core.Archives;

/// <summary>ZIP, TAR, TAR.GZ and 7Z are native. Everything else waits for an external tool (row 26).</summary>
public enum ArchiveKind
{
    Zip,
    Tar,
    TarGz,
    SevenZ,
    External,
}
