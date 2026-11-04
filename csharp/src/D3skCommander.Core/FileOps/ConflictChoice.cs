namespace D3skCommander.Core.FileOps;

/// <summary>
/// What to do when the target exists. Apply-to-all is <see cref="OverwriteAll"/>
/// and <see cref="SkipAll"/>. Rename and overwrite-older are a later row.
/// </summary>
public enum ConflictChoice
{
    Overwrite,
    Skip,
    Cancel,
    OverwriteAll,
    SkipAll,
}
