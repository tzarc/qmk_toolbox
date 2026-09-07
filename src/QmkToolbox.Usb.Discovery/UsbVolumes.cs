using System.Runtime.Versioning;

namespace QmkToolbox.Usb.Discovery;

/// <summary>
/// Enumerates the mounted volumes a USB device backs, for example the marker volume of a
/// mass-storage bootloader.
/// </summary>
public static class UsbVolumes
{
    /// <summary>
    /// Enumerates the mount points of the volumes provably backed by
    /// <paramref name="device"/>: Windows drive roots (<c>E:\</c>), Linux mount points
    /// (<c>/media/user/VOLUME</c>), or macOS volume paths (<c>/Volumes/VOLUME</c>), in
    /// platform enumeration order. Volumes whose ownership cannot be determined are not
    /// reported; <see cref="UsbVolumeOwner.OwnsVolume"/> distinguishes unknown from
    /// not-owned when that matters.
    /// </summary>
    /// <param name="device">The device to resolve, as delivered by
    /// <see cref="IUsbEventsDetector.DeviceConnected"/>.</param>
    public static IEnumerable<string> EnumerateVolumes(this UsbDeviceInfo device) =>
        OperatingSystem.IsWindows() ? EnumerateVolumesWindows(device) :
        OperatingSystem.IsLinux() ? EnumerateVolumesLinux(device) :
        OperatingSystem.IsMacOS() ? EnumerateVolumesMacOS(device) :
        [];

    /// <summary>
    /// Tests every ready drive rather than only removable ones: USB devices behind UASP
    /// enclosures enumerate as fixed disks, and ownership is the real filter.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static List<string> EnumerateVolumesWindows(UsbDeviceInfo device)
    {
        List<string> volumes = [];
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && device.OwnsVolume(drive.Name) == true)
                    volumes.Add(drive.Name);
            }
            catch (IOException)
            {
                // The drive vanished mid-enumeration; skip it.
            }
        }
        return volumes;
    }

    [SupportedOSPlatform("macos")]
    private static List<string> EnumerateVolumesMacOS(UsbDeviceInfo device)
    {
        const string volumesRoot = "/Volumes";
        if (!Directory.Exists(volumesRoot))
            return [];
        List<string> volumes = [];
        foreach (string mountPoint in Directory.EnumerateDirectories(volumesRoot))
        {
            if (device.OwnsVolume(mountPoint) == true)
                volumes.Add(mountPoint);
        }
        return volumes;
    }

    /// <summary>
    /// Scans a /proc/mounts-format table for volumes whose block device sits beneath the
    /// device's syspath. Entries appear in mount order.
    /// </summary>
    /// <param name="procMounts">Overrides the mount table (used by tests).</param>
    /// <param name="sysClassBlockRoot">Overrides the sysfs block class directory (used by tests).</param>
    internal static IEnumerable<string> EnumerateVolumesLinux(
        UsbDeviceInfo device, string procMounts = "/proc/mounts", string sysClassBlockRoot = "/sys/class/block")
    {
        if (!File.Exists(procMounts))
            yield break;

        foreach (string line in File.ReadLines(procMounts))
        {
            string[] parts = line.Split(' ');
            if (parts.Length < 2 || !parts[0].StartsWith("/dev/", StringComparison.Ordinal))
                continue;
            // /proc/mounts encodes spaces in paths as \040 (octal 040 = space).
            string mountPoint = parts[1].Replace("\\040", " ");
            if (UsbVolumeOwner.BelongsToLinux(mountPoint, device, procMounts, sysClassBlockRoot) == true)
                yield return mountPoint;
        }
    }
}
