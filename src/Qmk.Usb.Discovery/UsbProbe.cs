using Qmk.Usb.Discovery.Linux;
using Qmk.Usb.Discovery.MacOS;
using Qmk.Usb.Discovery.Windows;

namespace Qmk.Usb.Discovery;

/// <summary>
/// Creates the current platform's <see cref="IUsbProbe"/>, the raw event source an
/// <see cref="UsbDeviceTracker"/> runs over.
/// </summary>
public static class UsbProbe
{
    /// <summary>Creates the probe for the operating system this process is running on.</summary>
    /// <exception cref="PlatformNotSupportedException">The current platform has no probe.</exception>
    public static IUsbProbe CreateForCurrentPlatform()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsUsbProbe()
            : OperatingSystem.IsMacOS()
            ? new MacUsbProbe()
            : OperatingSystem.IsLinux()
            ? (IUsbProbe)new LinuxUsbProbe()
            : throw new PlatformNotSupportedException("USB detection supports Windows, Linux, and macOS.");
    }
}
