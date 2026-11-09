using D3skCommander.Core.FileOps;
using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.Core.Tests.FileOps;

public class FileOpGuardTests
{
    private const string Folder = "/data/work";

    [Theory]
    [InlineData(GuardedOp.Copy)]
    [InlineData(GuardedOp.Move)]
    [InlineData(GuardedOp.Paste)]
    [InlineData(GuardedOp.NewFolder)]
    public void WritingIntoComputerIsRefusedWhateverThePanelFolder(GuardedOp op)
    {
        Assert.True(FileOpGuard.IsRefused(op, Folder, ComputerAddress.Text));
        Assert.True(FileOpGuard.IsRefused(op, Folder, "  COMPUTER:/usb"));
        // Move is the one write op also refused from Computer (see ChangingItemsListedAtComputerIsRefused).
        Assert.Equal(op == GuardedOp.Move, FileOpGuard.IsRefused(op, ComputerAddress.Text, Folder));
    }

    [Theory]
    [InlineData(GuardedOp.Paste)]
    [InlineData(GuardedOp.NewFolder)]
    public void PasteAndNewFolderWithoutATargetWriteIntoTheActiveFolder(GuardedOp op)
    {
        Assert.True(FileOpGuard.IsRefused(op, ComputerAddress.Text));
        Assert.False(FileOpGuard.IsRefused(op, Folder));
    }

    [Theory]
    [InlineData(GuardedOp.Delete)]
    [InlineData(GuardedOp.Move)]
    [InlineData(GuardedOp.Cut)]
    [InlineData(GuardedOp.Rename)]
    public void ChangingItemsListedAtComputerIsRefused(GuardedOp op)
    {
        Assert.True(FileOpGuard.IsRefused(op, ComputerAddress.Text, Folder));
        Assert.False(FileOpGuard.IsRefused(op, Folder, Folder + "/sub"));
    }

    [Fact]
    public void CopyFromComputerToARealFolderIsAllowed()
    {
        Assert.False(FileOpGuard.IsRefused(GuardedOp.Copy, ComputerAddress.Text, Folder));
    }

    [Theory]
    [InlineData("computer:", true)]
    [InlineData("Computer:/x", true)]
    [InlineData("computer:x", false)]
    [InlineData("/data", false)]
    [InlineData(null, false)]
    public void ATargetFolderIsRefusedAtTheAddressOrUnderIt(string? target, bool refused)
    {
        Assert.Equal(refused, FileOpGuard.IsTargetRefused(target));
    }
}
