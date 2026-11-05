using D3skCommander.Core.Archives;

namespace D3skCommander.Core.Update;

/// <summary>Takes the program whose name matches the installed file out of a portable archive.</summary>
internal static class PortableUpdate
{
    public static UpdateStep<Relaunch> Install(string archive, string exe)
    {
        var found = Unpack(archive, exe);
        if (!found.IsSuccess || found.Value is null)
        {
            return UpdateStep<Relaunch>.Fail(found.Error ?? "not found in the update");
        }

        string? error = null;
        try
        {
            FileSwap.Replace(found.Value, exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }
        finally
        {
            FileSwap.DeleteDirectory(FileSwap.ScratchDirectory(exe));
        }

        if (error is not null)
        {
            return UpdateStep<Relaunch>.Fail(error);
        }

        return UpdateStep<Relaunch>.Ok(Relaunch.Of(exe));
    }

    private static UpdateStep<string> Unpack(string archive, string exe)
    {
        var name = Path.GetFileName(exe);
        if (string.IsNullOrEmpty(name))
        {
            return UpdateStep<string>.Fail("not found in the update");
        }

        var dir = FileSwap.ScratchDirectory(exe);
        FileSwap.DeleteDirectory(dir);
        var extracted = ArchiveIo.Extract(archive, null, dir);
        if (extracted.Error is not null)
        {
            return UpdateStep<string>.Fail(extracted.Error.Message);
        }

        var found = Find(dir, name);
        if (found is null)
        {
            return UpdateStep<string>.Fail(name + " not found in the update");
        }

        return UpdateStep<string>.Ok(found);
    }

    private static string? Find(string root, string name)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            if (string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal))
            {
                return path;
            }
        }

        return null;
    }
}
