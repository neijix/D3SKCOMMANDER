using D3skCommander.Core.Archives;
using D3skCommander.Core.SettingsLocation;
using D3skCommander.Core.Update;

namespace D3skCommander.Core.Install;

/// <summary>
/// Replaces an <c>install.sh</c> folder in place from the Linux tar.gz, keeping the old folder until the new one is in
/// place, then carries over the user's <c>config</c> subfolder and the install record (with the new version).
/// </summary>
internal static class LinuxScriptUpdate
{
    /// <summary>The Linux program file the publish writes (assembly name, no extension).</summary>
    private const string Program = "D3skCommander";

    public static UpdateStep<Relaunch> Apply(string archive, string folder, string? version)
    {
        var trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(parent) || !SettingsDirectory.CanWrite(parent) || !SettingsDirectory.CanWrite(trimmed))
        {
            return UpdateStep<Relaunch>.Fail(NotWritable(folder));
        }

        var scratch = FileSwap.ScratchDirectory(trimmed);
        FileSwap.DeleteDirectory(scratch);
        try
        {
            var extracted = ArchiveIo.Extract(archive, null, scratch);
            if (extracted.Error is not null)
            {
                return UpdateStep<Relaunch>.Fail(extracted.Error.Message);
            }

            var content = ContentRoot(scratch);
            Swap(content, trimmed);
            if (!string.IsNullOrEmpty(version))
            {
                InstallRecord.WriteVersion(trimmed, version);
            }

            return UpdateStep<Relaunch>.Ok(Relaunch.Of(Path.Combine(trimmed, Program)));
        }
        finally
        {
            FileSwap.DeleteDirectory(scratch);
        }
    }

    /// <summary>The English text; the UI catalog holds it as <c>update.not_writable</c> in both languages.</summary>
    public static string NotWritable(string folder) =>
        "Cannot update: " + folder + " is not writable. Move the app to a folder you own, or reinstall.";

    private static void Swap(string content, string folder)
    {
        var old = FileSwap.Sibling(folder, ".old");
        FileSwap.DeleteDirectory(old);
        Directory.Move(folder, old);
        try
        {
            Directory.Move(content, folder);
            Carry(old, folder);
        }
        catch (Exception)
        {
            FileSwap.DeleteDirectory(folder);
            Directory.Move(old, folder);
            throw;
        }

        FileSwap.DeleteDirectory(old);
    }

    private static void Carry(string old, string folder)
    {
        var config = Path.Combine(old, SettingsFileNames.Subfolder);
        if (Directory.Exists(config) && !Directory.Exists(Path.Combine(folder, SettingsFileNames.Subfolder)))
        {
            Directory.Move(config, Path.Combine(folder, SettingsFileNames.Subfolder));
        }

        var record = Path.Combine(old, InstallRecord.FileName);
        if (File.Exists(record))
        {
            File.Copy(record, Path.Combine(folder, InstallRecord.FileName), overwrite: true);
        }
    }

    /// <summary>The archive's single top-level folder when it has one, else the extraction folder itself.</summary>
    private static string ContentRoot(string scratch)
    {
        var entries = Directory.GetFileSystemEntries(scratch);
        return entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : scratch;
    }
}
