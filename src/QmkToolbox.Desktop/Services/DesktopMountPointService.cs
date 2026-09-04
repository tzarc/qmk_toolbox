using System.Runtime.Versioning;
using Qmk.Usb.Discovery;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Cross-platform mount point service for mass-storage bootloader devices (LUFA MS, UF2).
/// A volume qualifies when it carries the caller's marker file and
/// <see cref="UsbVolumeOwner"/> does not prove it belongs to a different USB device (unknown
/// ownership is accepted rather than rejecting a working volume). Among qualifying volumes the
/// most recently mounted wins.
/// <para>
/// Known limitation: when ownership is unresolvable and two devices of the same bootloader
/// family are mounted simultaneously, the newer volume is selected.
/// </para>
/// </summary>
public class DesktopMountPointService : IMountPointService
{
    public string? FindMountPoint(UsbDeviceInfo device, string markerFile) =>
        OperatingSystem.IsWindows() ? FindMountPointWindows(device, markerFile) :
        OperatingSystem.IsLinux() ? FindMountPointLinux(device, markerFile, "/proc/mounts") :
        OperatingSystem.IsMacOS() ? FindMountPointMacOS(device, markerFile) :
        null;

    private static bool HasMarker(string mountPoint, string markerFile) =>
        File.Exists(Path.Combine(mountPoint, markerFile));

    /// <summary>Returns the most recently created removable drive carrying the marker file and not provably another device's.</summary>
    [SupportedOSPlatform("windows")]
    private static string? FindMountPointWindows(UsbDeviceInfo device, string markerFile)
    {
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady && HasMarker(d.Name, markerFile))
            .Where(d => device.OwnsVolume(d.Name) != false)
            .Select(d => new DirectoryInfo(d.Name))
            .OrderByDescending(d => d.CreationTime)
            .FirstOrDefault()?.FullName.TrimEnd('\\', '/');
    }

    /// <summary>
    /// Scans a /proc/mounts-format table for the most recently mounted volume carrying the
    /// marker file and not provably another device's. Entries appear in mount order, so the
    /// last matching entry is the newest, so no timestamp comparison is needed.
    /// Matches mount points under /media/, /run/media/, and /mnt/ (covering udisks2-managed
    /// volumes on modern desktops as well as distros and setups that mount removable devices
    /// under /mnt/), which handles all USB mass-storage device node types (/dev/sd*,
    /// /dev/mmcblk*, /dev/vd*, etc.) without enumerating device-path prefixes.
    /// </summary>
    /// <param name="mountRoots">Overrides the accepted mount-point prefixes (used by tests).</param>
    internal static string? FindMountPointLinux(
        UsbDeviceInfo device, string markerFile, string procMounts, string[]? mountRoots = null)
    {
        mountRoots ??= ["/media/", "/run/media/", "/mnt/"];
        if (!File.Exists(procMounts))
            return null;

        string? newest = null;
        foreach (string line in File.ReadLines(procMounts))
        {
            string[] parts = line.Split(' ');
            if (parts.Length < 2)
                continue;
            // /proc/mounts encodes spaces in paths as \040 (octal 040 = space).
            string mountPoint = parts[1].Replace("\\040", " ");
            if (mountRoots.Any(r => mountPoint.StartsWith(r, StringComparison.Ordinal))
                && HasMarker(mountPoint, markerFile)
                && device.OwnsVolume(mountPoint) != false)
            {
                newest = mountPoint;
            }
        }
        return newest;
    }

    /// <summary>Returns the most recently created /Volumes entry carrying the marker file and not provably another device's.</summary>
    [SupportedOSPlatform("macos")]
    private static string? FindMountPointMacOS(UsbDeviceInfo device, string markerFile)
    {
        const string volumes = "/Volumes";
        return !Directory.Exists(volumes)
            ? null
            : (Directory.EnumerateDirectories(volumes)
            .Where(d => HasMarker(d, markerFile) && device.OwnsVolume(d) != false)
            .Select(d => new DirectoryInfo(d))
            .OrderByDescending(d => d.CreationTime)
            .FirstOrDefault()?.FullName);
    }
}
