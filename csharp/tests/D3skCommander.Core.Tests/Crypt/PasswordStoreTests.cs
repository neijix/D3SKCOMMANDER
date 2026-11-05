using D3skCommander.Core.Common;
using D3skCommander.Core.Crypt;
using Xunit;

namespace D3skCommander.Core.Tests.Crypt;

/// <summary>
/// P26: the five password-store tests of v0.1 <c>src/core/crypt.rs</c>, in the
/// <c>vault.d3enc</c> format. Saves use <see cref="EncryptionCost.Minimum"/> so the
/// file header carries a fast Argon2id cost; reopen reads that cost back.
/// </summary>
public class PasswordStoreTests
{
    [Fact]
    public void StoreSaveOpenRoundTrip()
    {
        using var scratch = new CryptScratch();
        var path = Path.Combine(scratch.Folder, PasswordStore.FileName);
        var opened = PasswordStore.Open(path, "master");
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        var store = opened.Value;
        Assert.Empty(store.Keys());
        store.Set("ftp:one", "p1");
        store.Set("ftp:two", "pä2");
        store.Remove("ftp:one");
        store.Set("ftp:three", "secret-three-value");
        Assert.True(store.Save(EncryptionCost.Minimum).IsSuccess);

        var raw = File.ReadAllBytes(path);
        Assert.False(Contains(raw, "ftp:two"u8));

        var reopened = PasswordStore.Open(path, "master");
        Assert.True(reopened.IsSuccess, reopened.Error?.Message);
        Assert.Equal(["ftp:three", "ftp:two"], reopened.Value.Keys());
        Assert.Equal("pä2", reopened.Value.Get("ftp:two"));
        Assert.Null(reopened.Value.Get("ftp:one"));
        Assert.DoesNotContain("secret-three-value", reopened.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StoreWrongMasterFails()
    {
        using var scratch = new CryptScratch();
        var path = Path.Combine(scratch.Folder, PasswordStore.FileName);
        var store = Opened(path, "master");
        store.Set("k", "v");
        Assert.True(store.Save(EncryptionCost.Minimum).IsSuccess);

        var failed = PasswordStore.Open(path, "other");
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorKind.AccessDenied, failed.Error!.Kind);
        Assert.Equal("wrong password", failed.Error.Message);
    }

    [Fact]
    public void StoreMissingFileIsEmptyAndSaveOverwritesAtomically()
    {
        using var scratch = new CryptScratch();
        var path = Path.Combine(scratch.Folder, "none.d3enc");
        var store = Opened(path, "m");
        Assert.Null(store.Get("k"));
        store.Set("k", "v1");
        Assert.True(store.Save(EncryptionCost.Minimum).IsSuccess);
        store.Set("k", "v2");
        Assert.True(store.Save(EncryptionCost.Minimum).IsSuccess);

        var reopened = PasswordStore.Open(path, "m");
        Assert.True(reopened.IsSuccess, reopened.Error?.Message);
        Assert.Equal("v2", reopened.Value.Get("k"));
        Assert.Single(Directory.GetFileSystemEntries(scratch.Folder));
    }

    [Fact]
    public void StoreFileIsOwnerOnly()
    {
        using var scratch = new CryptScratch();
        var path = Path.Combine(scratch.Folder, PasswordStore.FileName);
        var store = Opened(path, "m");
        store.Set("k", "v");
        Assert.True(store.Save(EncryptionCost.Minimum).IsSuccess);
        Assert.True(File.Exists(path));
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        const UnixFileMode Mask =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode & Mask);
    }

    [Fact]
    public void StoreCorruptFileIsInvalidData()
    {
        using var scratch = new CryptScratch();
        var path = Path.Combine(scratch.Folder, PasswordStore.FileName);
        File.WriteAllBytes(path, "garbage"u8.ToArray());
        var failed = PasswordStore.Open(path, "m");
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorKind.InvalidData, failed.Error!.Kind);
    }

    private static PasswordStore Opened(string path, string master)
    {
        var opened = PasswordStore.Open(path, master);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        return opened.Value;
    }

    private static bool Contains(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
