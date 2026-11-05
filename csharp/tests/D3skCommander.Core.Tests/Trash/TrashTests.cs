using D3skCommander.Core.Common;
using D3skCommander.Core.Trash;
using Xunit;

namespace D3skCommander.Core.Tests.Trash;

/// <summary>
/// P12: delete to the trash. Ports <c>delete_to_trash_removes_files_and_trees</c>
/// from <c>src/core/fileops/tests.rs</c>.
/// </summary>
public class TrashTests
{
    [Fact]
    public void DeleteToTrashRemovesFilesAndTrees()
    {
        var root = HomeFolder();
        try
        {
            var file = Path.Combine(root, "a.txt");
            var tree = Path.Combine(root, "tree");
            Write(file, "x");
            Write(Path.Combine(tree, "b.txt"), "b");

            var fileStatus = OsTrash.MoveToTrash(file);
            var treeStatus = OsTrash.MoveToTrash(tree);
            PurgeOwned(root);

            // v0.1 returns here when the volume has no usable trash (headless or tmpfs).
            if (fileStatus.OfferPermanentDelete && File.Exists(file))
            {
                return;
            }

            Assert.True(fileStatus.IsSuccess, fileStatus.Error?.Message);
            Assert.True(treeStatus.IsSuccess, treeStatus.Error?.Message);
            Assert.False(File.Exists(file));
            Assert.False(Directory.Exists(tree));
        }
        finally
        {
            PurgeOwned(root);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void UnsupportedMountReportsTrashUnavailable()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "libgio refuses the Linux temp mount");
        var root = Path.Combine(Path.GetTempPath(), "d3sk-trash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "a.txt");
        try
        {
            Write(file, "x");
            var status = OsTrash.MoveToTrash(file);
            if (status.IsSuccess)
            {
                PurgeOwned(root);
                Assert.Skip("this temp volume accepts trash");
            }

            Assert.True(status.OfferPermanentDelete);
            Assert.Equal(OsTrash.UnavailableMessage, status.Error!.Message);
            Assert.Equal(ErrorKind.Unsupported, status.Error.Kind);
            Assert.True(File.Exists(file));
            var offer = DeletePolicy.OfferPermanent(new DeleteSettings(UseTrash: true, ConfirmDelete: false));
            Assert.Equal(DeleteMode.Permanent, offer.Mode);
            Assert.True(offer.NeedsConfirmation);
        }
        finally
        {
            PurgeOwned(root);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void MissingPathIsNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), "d3sk-trash-missing-" + Guid.NewGuid().ToString("N"));

        var status = OsTrash.MoveToTrash(missing);

        Assert.False(status.IsSuccess);
        Assert.False(status.OfferPermanentDelete);
        Assert.Equal(ErrorKind.NotFound, status.Error!.Kind);
    }

    // The process temp dir is often a mount gio will not trash. The home volume will.
    private static string HomeFolder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var parent = string.IsNullOrEmpty(home) ? Path.GetTempPath() : Path.Combine(home, ".cache");
        var root = Path.Combine(parent, "d3sk-trash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Write(string path, string text)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, text);
    }

    // Test cleanup only: drop the items this test just trashed. The product does not read the trash spec.
    private static void PurgeOwned(string root)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(data))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            data = Path.Combine(home, ".local", "share");
        }

        var infoDir = Path.Combine(data, "Trash", "info");
        var filesDir = Path.Combine(data, "Trash", "files");
        if (!Directory.Exists(infoDir))
        {
            return;
        }

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var info in Directory.EnumerateFiles(infoDir, "*.trashinfo"))
        {
            if (!Owned(info, prefix))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(info);
            File.Delete(info);
            DeleteTrashed(Path.Combine(filesDir, stem));
        }
    }

    private static bool Owned(string infoPath, string prefix)
    {
        foreach (var line in File.ReadLines(infoPath))
        {
            if (!line.StartsWith("Path=", StringComparison.Ordinal))
            {
                continue;
            }

            var original = line["Path=".Length..];
            return original.StartsWith(prefix, StringComparison.Ordinal);
        }

        return false;
    }

    private static void DeleteTrashed(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.Directory) != 0 && (attrs & FileAttributes.ReparsePoint) == 0)
            {
                Directory.Delete(path, recursive: true);
                return;
            }

            File.Delete(path);
        }
    }
}
