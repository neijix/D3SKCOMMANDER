using System.Diagnostics;
using D3skCommander.Core.Preview;
using LibVLCSharp.Shared;

namespace D3skCommander.App.Features.QuickView;

/// <summary>
/// The libvlc of the VLC installed on the machine (<see cref="VlcLocator"/>), loaded the first time a video is
/// previewed and kept for the app's life. <see cref="Shared"/> is null when VLC is missing or does not load.
/// </summary>
internal static class VlcRuntime
{
    private static readonly Lock Gate = new();
    private static bool _tried;
    private static LibVLC? _vlc;
    private static VlcInstall? _install;

    /// <summary>The loaded libvlc, or null: the preview then asks to install VLC.</summary>
    public static LibVLC? Shared()
    {
        lock (Gate)
        {
            if (_tried)
            {
                return _vlc;
            }

            _tried = true;
            _install = VlcLocator.Find();
            if (_install is null)
            {
                return null;
            }

            try
            {
                if (_install.PluginFolder is { } plugins)
                {
                    Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", plugins);
                }

                LibVLCSharp.Shared.Core.Initialize(OperatingSystem.IsLinux() ? null : _install.LibraryFolder);
                _vlc = new LibVLC("--quiet", "--no-video-title-show", "--no-osd", "--no-stats", "--no-snapshot-preview");
            }
            catch (Exception exception) when (exception is VLCException or DllNotFoundException or BadImageFormatException
                or EntryPointNotFoundException or InvalidOperationException)
            {
                _vlc = null;
            }

            return _vlc;
        }
    }

    /// <summary>Opens <paramref name="path"/> in the VLC player itself; false when it could not start.</summary>
    public static bool OpenInPlayer(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var program = (_install ?? VlcLocator.Find())?.Program;
        if (program is null)
        {
            return false;
        }

        try
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false };
            info.ArgumentList.Add(path);
            using var started = Process.Start(info);
            return started is not null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
