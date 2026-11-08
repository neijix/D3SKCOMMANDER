using System.Diagnostics;

namespace D3skCommander.App.Features.Update.SignIn;

/// <summary>Opens a web address in the person's browser. Callers check <c>SignInPolicy.MayOpen</c> first.</summary>
public interface IUrlOpener
{
    void Open(string url);
}

/// <summary>Hands the address to the operating system's default browser.</summary>
public sealed class ProcessUrlOpener : IUrlOpener
{
    public void Open(string url)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        try
        {
            using var started = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // No browser to open: the window still shows the address and the code to type.
        }
    }
}
