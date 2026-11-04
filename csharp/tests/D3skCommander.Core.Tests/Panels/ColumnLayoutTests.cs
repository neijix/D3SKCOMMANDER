using D3skCommander.Core.Panels;
using Xunit;

namespace D3skCommander.Core.Tests.Panels;

public class ColumnLayoutTests
{
    [Fact]
    public void ColumnsDropAttrExtDateSizeInThatOrderWhenNarrow()
    {
        var all = Columns.Default;
        Assert.Equal(all, all.Fitting(2000f, 0f));
        var wide = Columns.NameMinimum
            + Columns.ExtensionWidth
            + Columns.SizeWidth
            + Columns.DateWidth
            + Columns.AttributesWidth;
        Assert.Equal(all, all.Fitting(wide, 0f));
        var noAttr = all.Fitting(wide - 1f, 0f);
        Assert.False(noAttr.Attributes);
        Assert.True(noAttr.Extension);
        Assert.True(noAttr.Size);
        Assert.True(noAttr.Date);
        var sizeDate = all.Fitting(80f + 80f + 130f, 0f);
        Assert.False(sizeDate.Extension);
        Assert.True(sizeDate.Size);
        Assert.True(sizeDate.Date);
        var sizeOnly = all.Fitting(200f, 0f);
        Assert.True(sizeOnly.Size);
        Assert.False(sizeOnly.Date);
        Assert.Equal(new Columns(false, false, false, false), all.Fitting(10f, 0f));
        var some = all with { Extension = false };
        Assert.False(some.Fitting(2000f, 0f).Extension);
    }
}
