using D3skCommander.Core.Common;
using D3skCommander.Core.Crypt;
using Xunit;

namespace D3skCommander.Core.Tests.Crypt;

/// <summary>
/// P26: the six non-store tests of v0.1 <c>src/core/crypt.rs</c>. Files are <c>.d3enc</c>,
/// not age. <c>WrongPasswordIsPermissionDenied</c> is <see cref="WrongPasswordIsRefused"/>;
/// <c>NotAnAgeFileIsInvalidData</c> is <see cref="NotOurFormatIsInvalidData"/>.
/// </summary>
public class CryptTests
{
    [Fact]
    public void FileRoundTrip()
    {
        using var scratch = new CryptScratch();
        var src = Path.Combine(scratch.Folder, "a.bin");
        var data = new byte[200_000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        File.WriteAllBytes(src, data);
        var enc = FileEncryption.DefaultEncryptedPath(src);
        Assert.Equal(Path.Combine(scratch.Folder, "a.bin.d3enc"), enc);

        long last = 0;
        var encrypted = FileEncryption.EncryptFile(src, enc, "pw é", n =>
        {
            last = n;
            return true;
        }, EncryptionCost.Minimum);
        Assert.True(encrypted.IsSuccess, encrypted.Error?.Message);
        Assert.Equal(data.Length, last);

        var cipher = File.ReadAllBytes(enc);
        Assert.Equal("D3ENC"u8.ToArray(), cipher[..5]);

        var outPath = Path.Combine(scratch.Folder, "out.bin");
        var decrypted = FileEncryption.DecryptFile(enc, outPath, "pw é", _ => true);
        Assert.True(decrypted.IsSuccess, decrypted.Error?.Message);
        Assert.Equal(data, File.ReadAllBytes(outPath));
    }

    [Fact]
    public void EmptyFileRoundTrip()
    {
        using var scratch = new CryptScratch();
        var src = Path.Combine(scratch.Folder, "e");
        File.WriteAllBytes(src, []);
        var enc = Path.Combine(scratch.Folder, "e.d3enc");
        var encrypted = FileEncryption.EncryptFile(src, enc, "x", _ => true, EncryptionCost.Minimum);
        Assert.True(encrypted.IsSuccess, encrypted.Error?.Message);

        var outPath = Path.Combine(scratch.Folder, "e.out");
        var decrypted = FileEncryption.DecryptFile(enc, outPath, "x", _ => true);
        Assert.True(decrypted.IsSuccess, decrypted.Error?.Message);
        Assert.Empty(File.ReadAllBytes(outPath));
    }

    [Fact]
    public void WrongPasswordIsRefused()
    {
        using var scratch = new CryptScratch();
        var src = Path.Combine(scratch.Folder, "a.txt");
        File.WriteAllBytes(src, "secret"u8.ToArray());
        var enc = Path.Combine(scratch.Folder, "a.txt.d3enc");
        var encrypted = FileEncryption.EncryptFile(src, enc, "right", _ => true, EncryptionCost.Minimum);
        Assert.True(encrypted.IsSuccess, encrypted.Error?.Message);

        var outPath = Path.Combine(scratch.Folder, "a.out");
        var failed = FileEncryption.DecryptFile(enc, outPath, "wrong", _ => true);
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorKind.AccessDenied, failed.Error!.Kind);
        Assert.Equal("wrong password", failed.Error.Message);
        Assert.False(File.Exists(outPath));
        Assert.False(File.Exists(Path.Combine(scratch.Folder, ".a.out.tmp")));
    }

    [Fact]
    public void NotOurFormatIsInvalidData()
    {
        using var scratch = new CryptScratch();
        var src = Path.Combine(scratch.Folder, "plain.txt");
        File.WriteAllBytes(src, "just text, not encrypted"u8.ToArray());
        var failed = FileEncryption.DecryptFile(src, Path.Combine(scratch.Folder, "o"), "x", _ => true);
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorKind.InvalidData, failed.Error!.Kind);
        Assert.Equal("not a valid encrypted file", failed.Error.Message);
    }

    [Fact]
    public void CancelLeavesNothingBehind()
    {
        using var scratch = new CryptScratch();
        var src = Path.Combine(scratch.Folder, "big");
        File.WriteAllBytes(src, new byte[300_000]);
        var enc = Path.Combine(scratch.Folder, "big.d3enc");
        var failed = FileEncryption.EncryptFile(src, enc, "x", _ => false, EncryptionCost.Minimum);
        Assert.False(failed.IsSuccess);
        Assert.Equal(ErrorKind.Cancelled, failed.Error!.Kind);
        Assert.Equal("cancelled", failed.Error.Message);
        Assert.Equal([src], Directory.GetFileSystemEntries(scratch.Folder));
    }

    [Fact]
    public void DefaultPaths()
    {
        var source = Path.Combine("x", "doc.txt");
        var encrypted = Path.Combine("x", "doc.txt.d3enc");
        Assert.Equal(encrypted, FileEncryption.DefaultEncryptedPath(source));
        Assert.Equal(source, FileEncryption.DefaultDecryptedPath(encrypted));
        Assert.Equal(source, FileEncryption.DefaultDecryptedPath(Path.Combine("x", "doc.txt.D3ENC")));
        Assert.Equal(Path.Combine("x", "blob.decrypted"), FileEncryption.DefaultDecryptedPath(Path.Combine("x", "blob")));
    }
}
