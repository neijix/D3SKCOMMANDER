using D3skCommander.Core.Tests.Providers;

namespace D3skCommander.Core.Tests.Ftp;

/// <summary>
/// The provider contract (<see cref="ProviderContractTests"/>) run on the FTP provider against the
/// in-process server. Every contract test takes its case from <c>Cases</c>, which this class
/// replaces with the FTP case. Row 86 may add <see cref="FtpCase"/> to the shared list instead.
/// </summary>
public class FtpProviderContractTests : ProviderContractTests
{
    public static new IEnumerable<object[]> Cases()
    {
        yield return [new FtpCase()];
    }
}
