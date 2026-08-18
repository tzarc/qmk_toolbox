using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

/// <summary>
/// The shared grammar for device-event diagnostic trace lines, used by both USB detectors and
/// the orchestrator so identifiers and paths format identically (and stay greppable) everywhere.
/// </summary>
public static class DeviceTrace
{
    /// <summary>Formats a device's VID/PID as <c>VID:XXXX PID:XXXX</c> (uppercase hex).</summary>
    public static string VidPid(IUsbDevice device) =>
        $"VID:{device.VendorId:X4} PID:{device.ProductId:X4}";

    /// <summary>
    /// Formats VID/PID plus revision as <c>VID:XXXX PID:XXXX REV:XXXX</c> — for arrival traces,
    /// where the revision has been read; removals never carry one.
    /// </summary>
    public static string VidPidRev(IUsbDevice device) =>
        $"{VidPid(device)} REV:{device.RevisionBcd:X4}";

    /// <summary>Quotes a device path, or <c>(empty)</c> when absent.</summary>
    public static string Path(string? path) =>
        string.IsNullOrEmpty(path) ? "(empty)" : $"\"{path}\"";
}
