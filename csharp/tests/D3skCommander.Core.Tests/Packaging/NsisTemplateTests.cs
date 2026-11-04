using Xunit;

namespace D3skCommander.Core.Tests.Packaging;

public class NsisTemplateTests
{
    [Fact]
    public void KeepsTheSettingsFolderPageAndItsDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packaging", "setup.nsi");
        var text = File.ReadAllText(path);
        Assert.Contains("Settings folder", text, StringComparison.Ordinal);
        Assert.Contains("MUI_DIRECTORYPAGE_VARIABLE $ConfigDir", text, StringComparison.Ordinal);
        Assert.Contains("$INSTDIR\\config", text, StringComparison.Ordinal);
        Assert.Contains("config-location.txt", text, StringComparison.Ordinal);
        Assert.Contains("FileWriteUTF16LE $0 \"config$\\r$\\n\"", text, StringComparison.Ordinal);
    }
}
