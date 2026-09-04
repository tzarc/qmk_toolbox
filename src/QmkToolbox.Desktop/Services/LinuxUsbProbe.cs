#if !WINDOWS
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Linux probe: udev-hex ID parsing, sysfs enrichment, and a /sys/bus/usb sweep of
/// already-present devices.
/// </summary>
internal sealed class LinuxUsbProbe : UsbEventsProbe
{
    private const string SysfsDevicesRoot = "/sys/bus/usb/devices";

    protected override bool TryParseId(string? s, out ushort value) =>
        UsbDeviceParser.TryParseUsbId(s, isMacOS: false, out value);

    protected override (ushort Revision, bool IsMassStorage) Enrich(ushort vid, ushort pid, string devicePath) =>
        (LinuxUsbSysfs.ReadBcdDevice(devicePath), LinuxUsbSysfs.HasMassStorageInterface(devicePath));

    public override IEnumerable<UsbDeviceInfo> EnumeratePresent() => EnumeratePresent(SysfsDevicesRoot);

    /// <summary>
    /// Walks a sysfs USB device directory: entries carrying idVendor/idProduct are device nodes
    /// (interface and endpoint entries have neither); hubs (bDeviceClass 09) are skipped, like
    /// the Windows sweep's device-interface filter. Symlinked entries resolve to the canonical
    /// /sys/devices/… syspath so swept devices dedup against later udev events for the same
    /// device.
    /// </summary>
    internal static IReadOnlyList<UsbDeviceInfo> EnumeratePresent(string sysfsRoot)
    {
        List<UsbDeviceInfo> devices = [];
        try
        {
            if (!Directory.Exists(sysfsRoot))
                return devices;
            foreach (string entry in Directory.EnumerateDirectories(sysfsRoot))
            {
                if (!UsbDeviceParser.TryParseUsbId(LinuxUsbSysfs.ReadAttribute(entry, "idVendor"), isMacOS: false, out ushort vid) ||
                    !UsbDeviceParser.TryParseUsbId(LinuxUsbSysfs.ReadAttribute(entry, "idProduct"), isMacOS: false, out ushort pid))
                {
                    continue;
                }

                if (LinuxUsbSysfs.ReadAttribute(entry, "bDeviceClass") == "09")
                    continue;

                string syspath = ResolveRealPath(entry);
                devices.Add(new UsbDeviceInfo(
                    vid, pid,
                    LinuxUsbSysfs.ReadBcdDevice(syspath),
                    LinuxUsbSysfs.ReadAttribute(entry, "manufacturer") ?? "",
                    LinuxUsbSysfs.ReadAttribute(entry, "product") ?? "",
                    "",
                    syspath,
                    LinuxUsbSysfs.HasMassStorageInterface(syspath)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed sweep must never break startup; hotplug events still work.
        }
        return devices;
    }

    private static string ResolveRealPath(string path)
    {
        try
        {
            return new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }
}
#endif
