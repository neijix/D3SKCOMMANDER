namespace D3skCommander.Core.FileOps;

/// <summary>
/// What to do when the target exists. <see cref="OverwriteAll"/>, <see cref="SkipAll"/>
/// and <see cref="OverwriteAllOlder"/> apply to the rest of the job.
/// <see cref="RenameNew"/> writes this file under a free name.
/// </summary>
public enum ConflictChoice
{
    Overwrite,
    Skip,
    Cancel,
    OverwriteAll,
    SkipAll,
    OverwriteAllOlder,
    RenameNew,
}
