namespace D3skCommander.Core.Archives;

/// <summary>ZIP, TAR, TAR.GZ and 7Z are native. RAR and the other formats use an installed <c>7z</c> or <c>unrar</c>.</summary>
public enum ArchiveKind
{
    Zip,
    Tar,
    TarGz,
    SevenZ,
    External,
}
