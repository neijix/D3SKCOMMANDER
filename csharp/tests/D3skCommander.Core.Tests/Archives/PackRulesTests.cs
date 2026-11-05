using D3skCommander.Core.Archives;
using Xunit;

namespace D3skCommander.Core.Tests.Archives;

/// <summary>P15 core: the pack dialog replaces a known archive extension. A6.</summary>
public class PackRulesTests
{
    [Fact]
    public void ArchiveExtensionIsReplaced()
    {
        Assert.Equal("/t/a.7z", PackRules.WithArchiveExtension("/t/a.zip", ArchiveKind.SevenZ));
        Assert.Equal("/t/a.zip", PackRules.WithArchiveExtension("/t/a.tar.gz", ArchiveKind.Zip));
        Assert.Equal("/t/a.tar", PackRules.WithArchiveExtension("/t/a", ArchiveKind.Tar));
    }
}
