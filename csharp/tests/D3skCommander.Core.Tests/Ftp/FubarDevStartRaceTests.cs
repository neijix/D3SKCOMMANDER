using Xunit;

namespace D3skCommander.Core.Tests.Ftp;

public sealed class FubarDevStartRaceTests
{
    [Fact]
    public void ADisposedSemaphoreOutsideTheFtpServerStillEndsTheRun()
    {
        var semaphore = new SemaphoreSlim(0, 1);
        semaphore.Dispose();

        var exception = Record.Exception(() => semaphore.Release());

        Assert.IsType<ObjectDisposedException>(exception);
        Assert.False(FubarDevStartRace.IsStartRace(exception));
    }

    [Fact]
    public void AnyOtherExceptionStillEndsTheRun()
    {
        Assert.False(FubarDevStartRace.IsStartRace(new InvalidOperationException("boom")));
    }
}
