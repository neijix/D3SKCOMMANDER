using D3skCommander.Testing;
using Xunit;

namespace D3skCommander.Core.Tests.Architecture;

public class SlowMarkerTests
{
    // Runs only under `csharp/check.sh --full`; proves the marker and the filter are wired.
    [Fact, Slow]
    public void SlowTierRunsOnlyInFullMode() => Assert.True(true);
}
