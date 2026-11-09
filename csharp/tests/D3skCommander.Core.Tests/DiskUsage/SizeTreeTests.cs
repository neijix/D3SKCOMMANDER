using D3skCommander.Core.DiskUsage;
using Xunit;

namespace D3skCommander.Core.Tests.DiskUsage;

public class SizeTreeTests
{
    [Fact]
    public void RemovingANodeUpdatesEveryAncestorAndReturnsItsParent()
    {
        var deep = SizeNode.File("deep.bin", "/r/a/b/deep.bin", 300);
        var keep = SizeNode.File("keep.bin", "/r/a/b/keep.bin", 50);
        var b = SizeNode.Folder("b", "/r/a/b", [deep, keep]);
        var a = SizeNode.Folder("a", "/r/a", [b, SizeNode.File("x.bin", "/r/a/x.bin", 10)]);
        var root = SizeNode.Folder("r", "/r", [a, SizeNode.File("y.bin", "/r/y.bin", 5)]);

        var (newRoot, parent) = SizeTree.Remove(root, deep);

        Assert.Equal(65, newRoot.Bytes);
        Assert.Equal(3, newRoot.Files);
        Assert.Equal("/r/a/b", parent.Path);
        Assert.Equal(["keep.bin"], parent.Children.Select(child => child.Name).ToArray());
        Assert.Same(newRoot, parent.Parent!.Parent);
        Assert.Equal(["b", "x.bin"], newRoot.Children[0].Children.Select(child => child.Name).ToArray());
    }

    [Fact]
    public void RemovingAChildOfTheRootReturnsTheNewRootAsParent()
    {
        var gone = SizeNode.File("gone.bin", "/r/gone.bin", 100);
        var root = SizeNode.Folder("r", "/r", [gone, SizeNode.File("k.bin", "/r/k.bin", 1)]);

        var (newRoot, parent) = SizeTree.Remove(root, gone);

        Assert.Same(newRoot, parent);
        Assert.Equal(1, newRoot.Bytes);
        Assert.Throws<ArgumentException>(() => SizeTree.Remove(root, root));
    }
}
