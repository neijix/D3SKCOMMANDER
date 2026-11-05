using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.Core.Tests.Localization;

public class ErrorLinesTests
{
    private static readonly Localizer French = Localizer.Load(settingsLanguage: Language.Fr, systemLanguage: Language.Fr);

    [Fact]
    public void AnErrorReadsAsItsLocalizedKindAndItsPath()
    {
        var line = ErrorLines.Describe(French, OperationError.NotFound("/tmp/x", "No such file or directory"));

        Assert.Equal(French.Get("error.not_found") + ": /tmp/x", line);
        Assert.DoesNotContain("No such file", line, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExceptionNeverShowsItsRawMessage()
    {
        var line = ErrorLines.Describe(French, new UnauthorizedAccessException("Access to the path is denied."), "/root/a");

        Assert.Equal(French.Get("error.access_denied") + ": /root/a", line);
    }

    [Fact]
    public void WithNoPathTheLineIsTheKindAlone()
    {
        var line = ErrorLines.Describe(French, new InvalidOperationException("internal state text"));

        Assert.Equal(French.Get("error.other"), line);
    }
}
