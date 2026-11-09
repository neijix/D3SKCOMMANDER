using D3skCommander.Core.Panels;

namespace D3skCommander.Core.FileOps;

/// <summary>A file operation the Computer view may refuse.</summary>
public enum GuardedOp
{
    Copy,
    Move,
    Paste,
    NewFolder,
    Delete,
    Cut,
    Rename,
}

/// <summary>
/// Decides when a file operation is refused because of Computer, the read-only level above the drives.
/// Pure: folder texts in, a verdict out; the caller shows <c>computer.readonly</c> before any job starts.
/// </summary>
public static class FileOpGuard
{
    /// <summary>
    /// Whether <paramref name="op"/> is refused. Writing ops (copy, move, paste, new folder) look at
    /// <paramref name="targetFolder"/> (paste and new folder fall back to <paramref name="activeFolder"/>);
    /// ops changing the listed items (delete, move, cut, rename) look at <paramref name="activeFolder"/>.
    /// Copy from Computer is allowed.
    /// </summary>
    public static bool IsRefused(GuardedOp op, string activeFolder, string? targetFolder = null)
    {
        var writes = op is GuardedOp.Copy or GuardedOp.Move or GuardedOp.Paste or GuardedOp.NewFolder;
        var changes = op is GuardedOp.Delete or GuardedOp.Move or GuardedOp.Cut or GuardedOp.Rename;
        if (writes)
        {
            // Paste and new folder write into the active folder whatever the target (a drive row's menu).
            var into = targetFolder ?? (op is GuardedOp.Paste or GuardedOp.NewFolder ? activeFolder : null);
            if (IsTargetRefused(into) || (op is GuardedOp.Paste or GuardedOp.NewFolder && IsTargetRefused(activeFolder)))
            {
                return true;
            }
        }

        return changes && ComputerAddress.IsAddress(activeFolder);
    }

    /// <summary>True when a chosen target folder is the Computer address or a path under it.</summary>
    public static bool IsTargetRefused(string? targetFolder) => ComputerAddress.IsAddressOrUnder(targetFolder);
}
