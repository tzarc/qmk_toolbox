using System.Runtime.Versioning;

using Qmk.Usb.Discovery.Linux;
using Qmk.Usb.Discovery.MacOS;
using Qmk.Usb.Discovery.Windows;

namespace Qmk.Usb.Discovery;

/// <summary>
/// Resolves whether a mounted volume is backed by a given USB device, for example to stop an
/// unrelated removable drive being mistaken for a device's own volume. Each platform walks its
/// own chain from the mount point to the owning USB device (drive → disk → parent devnode on
/// Windows, statfs → IOMedia → registry parents on macOS, mount source → sysfs block-device
/// ancestry on Linux); none of that reaches the interface.
/// </summary>
public static class UsbVolumeOwner
{
    /// <summary>
    /// Determines whether the volume mounted at <paramref name="mountPoint"/> is backed by
    /// <paramref name="device"/>.
    /// </summary>
    /// <param name="device">The candidate owning device, as delivered by
    /// <see cref="IUsbEventsDetector.DeviceConnected"/>.</param>
    /// <param name="mountPoint">The volume's mount point: a Windows drive root (<c>E:\</c>),
    /// a Linux mount point (<c>/media/user/VOLUME</c>), or a macOS volume path
    /// (<c>/Volumes/VOLUME</c>).</param>
    /// <returns><see langword="true"/> or <see langword="false"/> when ownership is provable;
    /// <see langword="null"/> when it cannot be determined (callers typically treat unknown as
    /// acceptable rather than rejecting a working volume).</returns>
    public static bool? OwnsVolume(this UsbDeviceInfo device, string mountPoint)
    {
        return OperatingSystem.IsWindows()
            ? BelongsToWindows(mountPoint, device)
            : OperatingSystem.IsLinux()
            ? BelongsToLinux(mountPoint, device, "/proc/mounts", "/sys/class/block")
            : OperatingSystem.IsMacOS() ? BelongsToMacOS(mountPoint, device) : null;
    }

    [SupportedOSPlatform("windows")]
    private static bool? BelongsToWindows(string mountPoint, UsbDeviceInfo device)
    {
        if (device.DevicePath.Length == 0)
            return null; // no identity to compare against
        string? owner = WindowsVolumeOwner.GetOwningUsbInstanceId(mountPoint);
        return owner == null
            ? null
            : string.Equals(owner, UsbDeviceParser.InterfacePathToInstanceId(device.DevicePath), StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("macos")]
    private static bool? BelongsToMacOS(string mountPoint, UsbDeviceInfo device)
    {
        if (MacUsbRegistry.FindVolumeOwner(mountPoint) is not { } owner)
            return null;

        // Registry paths are exact; VID/PID equality covers a path-format mismatch between the
        // hotplug event's path and IORegistryEntryGetPath (identical devices then stay
        // indistinguishable; ownership between them is genuinely ambiguous).
        return (device.DevicePath.Length > 0 && owner.DevicePath == device.DevicePath)
            || (owner.VendorId == device.VendorId && owner.ProductId == device.ProductId);
    }

    internal static bool? BelongsToLinux(string mountPoint, UsbDeviceInfo device, string procMounts, string sysClassBlockRoot)
    {
        if (device.DevicePath.Length == 0)
            return null; // no identity to compare against

        string? source = FindMountSource(mountPoint, procMounts);
        if (source == null || !source.StartsWith("/dev/", StringComparison.Ordinal))
            return null;

        string? blockSyspath = LinuxUsbSysfs.ResolveRealPath(Path.Combine(sysClassBlockRoot, Path.GetFileName(source)));
        if (blockSyspath == null)
            return null;

        // The block device's canonical syspath sits beneath its USB device's syspath, e.g.
        // /sys/devices/…/usb3/3-1/3-1:1.0/host6/…/block/sdb/sdb1 under /sys/devices/…/usb3/3-1.
        return blockSyspath.StartsWith(device.DevicePath + "/", StringComparison.Ordinal);
    }

    /// <summary>Finds the mount source (e.g. <c>/dev/sdb1</c>) for a mount point in a /proc/mounts-format table.</summary>
    private static string? FindMountSource(string mountPoint, string procMounts)
    {
        try
        {
            if (!File.Exists(procMounts))
                return null;
            string? source = null;
            foreach (string line in File.ReadLines(procMounts))
            {
                string[] parts = line.Split(' ');
                // /proc/mounts encodes spaces in paths as \040 (octal 040 = space).
                if (parts.Length >= 2 && parts[1].Replace("\\040", " ") == mountPoint)
                    source = parts[0]; // entries appear in mount order; the last match is current
            }
            return source;
        }
        catch (IOException)
        {
            return null;
        }
    }

}
