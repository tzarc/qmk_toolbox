using System.Runtime.Versioning;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Cross-platform mount point service for mass-storage bootloader devices (LUFA MS, UF2).
/// A volume qualifies when it carries the caller's marker file and is not provably backed by a
/// different USB device: each platform resolves the volume's owning device (sysfs ancestry on
/// Linux, disk→parent devnode chain on Windows, IOMedia parent chain on macOS) and volumes whose
/// ownership cannot be determined are accepted rather than rejected. Among qualifying volumes
/// the most recently mounted wins.
/// <para>
/// Known limitation: when ownership is unresolvable and two devices of the same bootloader
/// family are mounted simultaneously, the newer volume is selected.
/// </para>
/// </summary>
public class DesktopMountPointService : IMountPointService
{
    public string? FindMountPoint(IUsbDevice device, string markerFile) =>
        OperatingSystem.IsWindows() ? FindMountPointWindows(device, markerFile) :
        OperatingSystem.IsLinux() ? FindMountPointLinux(device, markerFile, "/proc/mounts", "/sys/class/block") :
        OperatingSystem.IsMacOS() ? FindMountPointMacOS(device, markerFile) :
        null;

    private static bool HasMarker(string mountPoint, string markerFile) =>
        File.Exists(Path.Combine(mountPoint, markerFile));

    /// <summary>Returns the most recently created removable drive carrying the marker file and belonging to the device.</summary>
    [SupportedOSPlatform("windows")]
    private static string? FindMountPointWindows(IUsbDevice device, string markerFile)
    {
        string expectedInstanceId = UsbDeviceParser.InterfacePathToInstanceId(device.DevicePath);
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady && HasMarker(d.Name, markerFile))
            .Where(d => BelongsToDeviceWindows(d.Name, device.DevicePath, expectedInstanceId))
            .Select(d => new DirectoryInfo(d.Name))
            .OrderByDescending(d => d.CreationTime)
            .FirstOrDefault()?.FullName.TrimEnd('\\', '/');
    }

    [SupportedOSPlatform("windows")]
    private static bool BelongsToDeviceWindows(string driveRoot, string devicePath, string expectedInstanceId)
    {
        if (devicePath.Length == 0)
            return true; // no identity to compare against — ownership unknown
        string? owner = WindowsVolumeOwner.GetOwningUsbInstanceId(driveRoot);
        return owner == null || string.Equals(owner, expectedInstanceId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans a /proc/mounts-format table for the most recently mounted volume carrying the
    /// marker file and belonging to the device. Entries appear in mount order, so the last
    /// matching entry is the newest, so no timestamp comparison is needed.
    /// Matches mount points under /media/, /run/media/, and /mnt/ (covering udisks2-managed
    /// volumes on modern desktops as well as distros and setups that mount removable devices
    /// under /mnt/), which handles all USB mass-storage device node types (/dev/sd*,
    /// /dev/mmcblk*, /dev/vd*, etc.) without enumerating device-path prefixes.
    /// Ownership: the mount source's /sys/class/block entry resolves to a syspath beneath the
    /// owning USB device's syspath; a volume resolving beneath a different device is skipped.
    /// </summary>
    /// <param name="mountRoots">Overrides the accepted mount-point prefixes (used by tests).</param>
    internal static string? FindMountPointLinux(
        IUsbDevice device, string markerFile, string procMounts, string sysClassBlockRoot, string[]? mountRoots = null)
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
                && BelongsToDeviceLinux(parts[0], device.DevicePath, sysClassBlockRoot))
            {
                newest = mountPoint;
            }
        }
        return newest;
    }

    private static bool BelongsToDeviceLinux(string mountSource, string deviceSyspath, string sysClassBlockRoot)
    {
        if (deviceSyspath.Length == 0 || !mountSource.StartsWith("/dev/", StringComparison.Ordinal))
            return true; // no identity to compare against — ownership unknown

        string? blockSyspath = ResolveLinkTarget(Path.Combine(sysClassBlockRoot, Path.GetFileName(mountSource)));
        if (blockSyspath == null)
            return true; // unresolvable — ownership unknown

        // The block device's canonical syspath sits beneath its USB device's syspath, e.g.
        // /sys/devices/…/usb3/3-1/3-1:1.0/host6/…/block/sdb/sdb1 under /sys/devices/…/usb3/3-1.
        return blockSyspath.StartsWith(deviceSyspath + "/", StringComparison.Ordinal);
    }

    private static string? ResolveLinkTarget(string path)
    {
        try
        {
            if (new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName is { } resolved)
                return resolved;
            // Not a symlink: a real directory still identifies the block device.
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Returns the most recently created /Volumes entry carrying the marker file and belonging to the device.</summary>
    [SupportedOSPlatform("macos")]
    private static string? FindMountPointMacOS(IUsbDevice device, string markerFile)
    {
        const string volumes = "/Volumes";
        return !Directory.Exists(volumes)
            ? null
            : (Directory.EnumerateDirectories(volumes)
            .Where(d => HasMarker(d, markerFile) && BelongsToDeviceMacOS(d, device))
            .Select(d => new DirectoryInfo(d))
            .OrderByDescending(d => d.CreationTime)
            .FirstOrDefault()?.FullName);
    }

    [SupportedOSPlatform("macos")]
    private static bool BelongsToDeviceMacOS(string mountPath, IUsbDevice device)
    {
        if (MacUsbRegistry.FindVolumeOwner(mountPath) is not { } owner)
            return true; // unresolvable — ownership unknown

        // Registry paths are exact; VID/PID equality covers a path-format mismatch between the
        // hotplug event's path and IORegistryEntryGetPath (identical boards then stay ambiguous,
        // per the documented limitation).
        return (device.DevicePath.Length > 0 && owner.DevicePath == device.DevicePath)
            || (owner.VendorId == device.VendorId && owner.ProductId == device.ProductId);
    }
}
