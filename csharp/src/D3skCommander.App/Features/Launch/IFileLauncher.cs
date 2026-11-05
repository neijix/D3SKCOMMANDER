using D3skCommander.Core.Launch;

namespace D3skCommander.App.Features.Launch;

/// <summary>Opens a file with the system default application. A failure is shown to the user.</summary>
public interface IFileLauncher
{
    LaunchResult OpenDefault(string path);
}
