using D3skCommander.Core.Archives;
using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;
using D3skCommander.Core.Providers;
using D3skCommander.Core.Tests.Archives;
using Xunit;

namespace D3skCommander.Core.Tests.Providers;

public class ArchiveProviderTests
{
    [Fact]
    public void CopyOutExtractsTheMember()
    {
        using var fixture = new ArchiveProviderFixture();
        var destination = Path.Combine(Path.GetDirectoryName(fixture.ArchivePath)!, "out");
        var copied = ArchiveTransfers.CopyOut(fixture.SampleFilePath, destination, CancellationToken.None);
        Assert.True(copied.IsExtract, copied.Error?.Message);
        Assert.Null(copied.Error);
        Assert.Equal(fixture.SampleBytes, File.ReadAllBytes(Path.Combine(destination, fixture.SampleFileName)));
        Assert.True(File.Exists(fixture.ArchivePath));
    }

    [Fact]
    public void MoveOutIsRefused()
    {
        using var fixture = new ArchiveProviderFixture();
        var moved = ArchiveTransfers.MoveOut(fixture.SampleFilePath);
        Assert.False(moved.IsExtract);
        AssertRefusal(moved, ArchiveTransfers.MoveOutCode, ArchiveTransfers.MoveOutEnglish, ArchiveTransfers.MoveOutFrench);
        Assert.Equal(fixture.SampleFilePath, moved.Error?.Path);
    }

    [Fact]
    public void CopyInTellsTheUserToPack()
    {
        using var fixture = new ArchiveProviderFixture();
        var refused = ArchiveTransfers.CopyIn(fixture.Directory);
        Assert.False(refused.IsExtract);
        AssertRefusal(refused, ArchiveTransfers.UsePackCode, ArchiveTransfers.UsePackEnglish, ArchiveTransfers.UsePackFrench);
        Assert.Contains("Pack", refused.English, StringComparison.Ordinal);
        Assert.Contains("Compresser", refused.French, StringComparison.Ordinal);
        Assert.NotEqual(refused.English, refused.French);
        Assert.Equal(fixture.Directory, refused.Error?.Path);
    }

    [Fact]
    public void UnsafeMemberNamesStayRefused()
    {
        var root = Directory.CreateTempSubdirectory("d3sk-slip-").FullName;
        try
        {
            var zip = Path.Combine(root, "slip.zip");
            ArchiveSlip.WriteZip(zip, "../evil.txt", [1, 2, 3]);
            var destination = Path.Combine(root, "out");
            var copied = ArchiveTransfers.Extract(zip, ["../evil.txt"], destination, CancellationToken.None);
            Assert.False(copied.IsExtract);
            Assert.NotNull(copied.Error);
            Assert.Equal(ErrorKind.InvalidData, copied.Error.Kind);
            Assert.Contains("unsafe path", copied.Error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, "evil.txt")));
            Assert.False(File.Exists(Path.Combine(destination, "evil.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertRefusal(ArchiveTransfer transfer, string code, string english, string french)
    {
        Assert.NotNull(transfer.Error);
        Assert.Equal(ErrorKind.Unsupported, transfer.Error.Kind);
        Assert.Equal(code, transfer.Error.Message);
        Assert.Equal(code, transfer.Code);
        Assert.Equal(english, transfer.English);
        Assert.Equal(french, transfer.French);
        var localizer = Localizer.Load(Language.En);
        Assert.Equal(english, transfer.Text(localizer));
        localizer.Use(Language.Fr);
        Assert.Equal(french, transfer.Text(localizer));
    }
}
