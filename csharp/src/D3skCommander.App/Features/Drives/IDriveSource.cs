using D3skCommander.Core.FileSystem;

namespace D3skCommander.App.Features.Drives;

/// <summary>The volumes of the machine. The commands use this seam so tests do not depend on the host.</summary>
public interface IDriveSource
{
    IReadOnlyList<DriveEntry> Mounted();
}

/// <summary>Asks the OS through <see cref="DriveList"/>.</summary>
public sealed class OsDriveSource : IDriveSource
{
    public IReadOnlyList<DriveEntry> Mounted() => DriveList.Mounted();
}
