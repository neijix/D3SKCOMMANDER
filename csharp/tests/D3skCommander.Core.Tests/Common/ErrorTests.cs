using System.Net.Sockets;
using D3skCommander.Core.Common;
using D3skCommander.Core.Localization;
using Xunit;

namespace D3skCommander.Core.Tests.Common;

public class ErrorTests
{
    [Fact]
    public void EachKindIsDistinct()
    {
        Assert.Equal(
            [
                ErrorKind.NotFound,
                ErrorKind.AccessDenied,
                ErrorKind.AlreadyExists,
                ErrorKind.Cancelled,
                ErrorKind.InvalidData,
                ErrorKind.Unsupported,
                ErrorKind.Network,
                ErrorKind.Other,
            ],
            Enum.GetValues<ErrorKind>());

        Assert.Equal(ErrorKind.NotFound, OperationError.NotFound("a.txt").Kind);
        Assert.Equal("a.txt", OperationError.NotFound("a.txt").Path);
        Assert.Equal("not found", OperationError.NotFound("a.txt").Message);
        Assert.Equal(ErrorKind.AccessDenied, OperationError.AccessDenied("wrong password", "secret.d3enc").Kind);
        Assert.Equal(ErrorKind.AlreadyExists, OperationError.AlreadyExists("a.txt").Kind);
        Assert.Equal("already exists", OperationError.AlreadyExists("a.txt").Message);
        Assert.Equal(ErrorKind.Cancelled, OperationError.Cancelled().Kind);
        Assert.Equal("cancelled", OperationError.Cancelled().Message);
        Assert.Equal(ErrorKind.InvalidData, OperationError.InvalidData("corrupt password store").Kind);
        Assert.Equal(ErrorKind.Unsupported, OperationError.Unsupported("unknown archive type: x").Kind);
        Assert.Equal(ErrorKind.Network, OperationError.Network("download failed").Kind);
        Assert.Equal(ErrorKind.Other, OperationError.Other("dpkg failed").Kind);
    }

    [Theory]
    [InlineData("cancelled", ErrorKind.Cancelled)]
    [InlineData("dir/a: cancelled", ErrorKind.Cancelled)]
    [InlineData("a file with that name already exists", ErrorKind.AlreadyExists)]
    [InlineData("/tmp/a already exists", ErrorKind.AlreadyExists)]
    [InlineData("foo.001 not found in /tmp", ErrorKind.NotFound)]
    [InlineData("wrong password", ErrorKind.AccessDenied)]
    [InlineData("the archive is password protected", ErrorKind.AccessDenied)]
    [InlineData("administrator rights refused — or run: sudo", ErrorKind.AccessDenied)]
    [InlineData("unknown archive type: foo.rar", ErrorKind.Unsupported)]
    [InlineData("this format cannot be created", ErrorKind.Unsupported)]
    [InlineData("symlinks are not copied on this platform", ErrorKind.Unsupported)]
    [InlineData("network drives are not supported on this system", ErrorKind.Unsupported)]
    [InlineData("unsupported protocol: s3://", ErrorKind.Unsupported)]
    [InlineData("this archive format needs an external tool: install 7-Zip (7z) or unrar", ErrorKind.Unsupported)]
    [InlineData("built from source: update with git pull, then cargo build", ErrorKind.Unsupported)]
    [InlineData("download failed: connection reset", ErrorKind.Network)]
    [InlineData("upload failed: timeout", ErrorKind.Network)]
    [InlineData("corrupt password store", ErrorKind.InvalidData)]
    [InlineData("not a valid encrypted file: header", ErrorKind.InvalidData)]
    [InlineData("empty catalog file", ErrorKind.InvalidData)]
    [InlineData("no host given", ErrorKind.InvalidData)]
    [InlineData("invalid time offset: abc", ErrorKind.InvalidData)]
    [InlineData("not a directory: /tmp", ErrorKind.InvalidData)]
    [InlineData("not a drive letter: Z", ErrorKind.InvalidData)]
    [InlineData("no file name", ErrorKind.InvalidData)]
    [InlineData("nothing to launch: empty command or no files", ErrorKind.InvalidData)]
    [InlineData("empty command line", ErrorKind.InvalidData)]
    [InlineData("missing server name", ErrorKind.InvalidData)]
    [InlineData("dpkg failed (1) — try: sudo dpkg -i", ErrorKind.Other)]
    [InlineData("copied, but the source could not be removed: busy", ErrorKind.Other)]
    public void V01MessagesMapToTheKindTheyReported(string message, ErrorKind kind)
    {
        var error = OperationError.FromMessage(message);
        Assert.Equal(kind, error.Kind);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void ExceptionsMapToTheKindV01Reported()
    {
        var missing = OperationError.FromException(new FileNotFoundException("missing", "a.txt"));
        Assert.Equal(ErrorKind.NotFound, missing.Kind);
        Assert.Equal("a.txt", missing.Path);

        var directory = OperationError.FromException(new DirectoryNotFoundException("missing"), "dir");
        Assert.Equal(ErrorKind.NotFound, directory.Kind);
        Assert.Equal("dir", directory.Path);

        Assert.Equal(ErrorKind.AccessDenied, OperationError.FromException(new UnauthorizedAccessException("denied")).Kind);

        var exists = new IOException("exists", unchecked((int)0x800700B7));
        var already = OperationError.FromException(exists);
        Assert.Equal(ErrorKind.AlreadyExists, already.Kind);
        Assert.Equal(exists.HResult, already.OsError);

        var sharing = new IOException("busy", unchecked((int)0x80070020));
        Assert.Equal(ErrorKind.AccessDenied, OperationError.FromException(sharing).Kind);

        var byText = OperationError.FromException(new IOException("a file with that name already exists"));
        Assert.Equal(ErrorKind.AlreadyExists, byText.Kind);

        Assert.Equal(ErrorKind.Cancelled, OperationError.FromException(new OperationCanceledException("cancelled")).Kind);
        Assert.Equal(ErrorKind.InvalidData, OperationError.FromException(new InvalidDataException("corrupt password store")).Kind);
        Assert.Equal(ErrorKind.InvalidData, OperationError.FromException(new EndOfStreamException("truncated")).Kind);
        Assert.Equal(ErrorKind.InvalidData, OperationError.FromException(new FormatException("bad")).Kind);
        Assert.Equal(
            ErrorKind.Unsupported,
            OperationError.FromException(new NotSupportedException("this format cannot be created")).Kind);
        Assert.Equal(
            ErrorKind.Network,
            OperationError.FromException(new SocketException((int)SocketError.ConnectionRefused)).Kind);
        Assert.Equal(ErrorKind.Network, OperationError.FromException(new HttpRequestException("download failed")).Kind);

        var wrapped = new IOException("outer", new SocketException((int)SocketError.HostUnreachable));
        Assert.Equal(ErrorKind.Network, OperationError.FromException(wrapped).Kind);

        var networkCode = new IOException("unreachable", unchecked((int)0x800704CF));
        Assert.Equal(ErrorKind.Network, OperationError.FromException(networkCode).Kind);

        Assert.Equal(ErrorKind.Other, OperationError.FromException(new Exception("dpkg failed (1)")).Kind);
    }

    [Fact]
    public void ItemReportCarriesTheItemTheErrorAndWhetherItIsRetryable()
    {
        var error = OperationError.Network("download failed", "remote/a.txt");
        var retry = new ItemReport("remote/a.txt", error, retryable: true);
        Assert.Equal("remote/a.txt", retry.Item);
        Assert.Same(error, retry.Error);
        Assert.Equal(ErrorKind.Network, retry.Error.Kind);
        Assert.True(retry.Retryable);

        var permanent = new ItemReport("bad.dat", OperationError.InvalidData("corrupt"), retryable: false);
        Assert.Equal("bad.dat", permanent.Item);
        Assert.Equal(ErrorKind.InvalidData, permanent.Error.Kind);
        Assert.False(permanent.Retryable);
    }

    [Fact]
    public void EveryKindHasBilingualText()
    {
        var localizer = Localizer.Load(settingsLanguage: Language.En, systemLanguage: Language.En);
        foreach (var kind in Enum.GetValues<ErrorKind>())
        {
            var key = ErrorText.Key(kind);
            var english = localizer.Get(key, Language.En);
            var french = localizer.Get(key, Language.Fr);
            Assert.NotEqual(key, english);
            Assert.NotEqual(key, french);
            Assert.NotEqual(english, french);
        }
    }
}
