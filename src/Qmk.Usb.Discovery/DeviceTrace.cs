
namespace Qmk.Usb.Discovery;

/// <summary>
/// Formats device identifiers and paths for diagnostic trace lines. Every trace producer uses
/// these helpers, so trace lines format identically and stay greppable.
/// </summary>
public static class DeviceTrace
{
    /// <summary>Formats a device's VID/PID as <c>VID:XXXX PID:XXXX</c> (uppercase hex).</summary>
    public static string VidPid(UsbDeviceInfo device) =>
        $"VID:{device.VendorId:X4} PID:{device.ProductId:X4}";

    /// <summary>
    /// Formats VID/PID plus revision as <c>VID:XXXX PID:XXXX REV:XXXX</c>, for arrival traces
    /// where the revision has been read; removals never carry one.
    /// </summary>
    public static string VidPidRev(UsbDeviceInfo device) =>
        $"{VidPid(device)} REV:{device.RevisionBcd:X4}";

    /// <summary>Quotes a device path, or <c>(empty)</c> when absent.</summary>
    public static string Path(string? path) =>
        string.IsNullOrEmpty(path) ? "(empty)" : $"\"{path}\"";
}
