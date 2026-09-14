using System.Runtime.Versioning;

using static Interop.CoreFoundation;
using static Interop.IOKit;
using static Interop.LibSystem;

namespace QmkToolbox.Usb.Discovery.MacOS;

/// <summary>
/// Reads USB device properties and topology from the macOS IOKit registry: arrival payloads
/// straight off a device's <c>io_service_t</c>, the mass-storage interface check,
/// present-device enumeration for the startup sweep, and which device backs a mounted volume.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacUsbRegistry
{
    /// <summary>
    /// Builds the arrival payload for a USB device service: identity, revision, strings, and
    /// registry path straight off the entry; the mass-storage flag via the interface query
    /// (with its settle window for late-registering interface nubs).
    /// </summary>
    internal static UsbDeviceInfo BuildDeviceInfo(uint service)
    {
        ushort vid = ReadUShortProperty(service, "idVendor");
        ushort pid = ReadUShortProperty(service, "idProduct");
        return new UsbDeviceInfo(
            vid, pid,
            ReadUShortProperty(service, "bcdDevice"),
            ReadStringProperty(service, "USB Vendor Name") ?? "",
            ReadStringProperty(service, "USB Product Name") ?? "",
            RegistryPath(service),
            HasMassStorageInterface(vid, pid));
    }

    /// <summary>True for hubs (bDeviceClass 09) and entries without a usable VID/PID identity.</summary>
    internal static bool ShouldSkipDevice(uint service)
    {
        ushort vid = ReadUShortProperty(service, "idVendor");
        ushort pid = ReadUShortProperty(service, "idProduct");
        return (vid == 0 && pid == 0) || ReadUShortProperty(service, "bDeviceClass") == 0x09;
    }

    /// <summary>Identity of a (possibly terminated) service, for removal matching.</summary>
    internal static (ushort VendorId, ushort ProductId, string DevicePath) ReadIdentity(uint service) =>
        (ReadUShortProperty(service, "idVendor"), ReadUShortProperty(service, "idProduct"), RegistryPath(service));

    /// <summary>
    /// Returns true when any USB interface of the device matching the VID/PID reports
    /// <c>bInterfaceClass</c> 08 (mass storage). Interface nubs are registered slightly
    /// after the device arrival notification, so this waits briefly until at least one
    /// interface for the device is visible (or the settle window runs out) before deciding.
    /// </summary>
    public static bool HasMassStorageInterface(ushort vendorId, ushort productId)
    {
        const int attempts = 5;
        const int delayMs = 100;
        try
        {
            for (int i = 0; i < attempts; i++)
            {
                int seen = 0;
                // .NET 10 requires macOS 13+, where interface nubs are always IOUSBHostInterface.
                if (QueryInterfaces("IOUSBHostInterface", vendorId, productId, ref seen))
                {
                    return true;
                }
                if (seen > 0)
                    return false;
                if (i < attempts - 1)
                    Thread.Sleep(delayMs);
            }
        }
        catch (Exception)
        {
            // A failed registry lookup must never break device detection.
        }
        return false;
    }

    private static bool QueryInterfaces(string className, ushort vendorId, ushort productId, ref int seen)
    {
        IntPtr matching = IOServiceMatching(className);
        if (matching == IntPtr.Zero)
            return false;
        if (IOServiceGetMatchingServices(KIOMainPortDefault, matching, out uint iterator) != 0 || iterator == 0)
            return false;
        try
        {
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            {
                try
                {
                    if (ReadUShortProperty(service, "idVendor") == vendorId &&
                        ReadUShortProperty(service, "idProduct") == productId)
                    {
                        seen++;
                        if (ReadUShortProperty(service, "bInterfaceClass") == 0x08)
                            return true;
                    }
                }
                finally
                {
                    _ = IOObjectRelease(service);
                }
            }
        }
        finally
        {
            _ = IOObjectRelease(iterator);
        }
        return false;
    }

    /// <summary>
    /// Enumerates the USB devices present right now, for the tracker's startup sweep. Hubs
    /// (bDeviceClass 09) are skipped. The device path is the IOService registry path; when it
    /// differs from the hotplug event's path, removal matching falls back to VID/PID.
    /// </summary>
    public static List<UsbDeviceInfo> EnumeratePresentDevices()
    {
        List<UsbDeviceInfo> devices = [];
        try
        {
            // .NET 10 requires macOS 13+, where devices are always IOUSBHostDevice.
            IntPtr matching = IOServiceMatching("IOUSBHostDevice");
            if (matching == IntPtr.Zero)
                return devices;
            if (IOServiceGetMatchingServices(KIOMainPortDefault, matching, out uint iterator) != 0 || iterator == 0)
                return devices;
            try
            {
                uint service;
                while ((service = IOIteratorNext(iterator)) != 0)
                {
                    try
                    {
                        if (!ShouldSkipDevice(service))
                            devices.Add(BuildDeviceInfo(service));
                    }
                    finally
                    {
                        _ = IOObjectRelease(service);
                    }
                }
            }
            finally
            {
                _ = IOObjectRelease(iterator);
            }
        }
        catch (Exception)
        {
            // A failed sweep must never break startup; hotplug events still work.
        }
        return devices;
    }

    /// <summary>
    /// Resolves the USB device carrying the volume mounted at <paramref name="mountPath"/>
    /// (e.g. <c>/Volumes/RPI-RP2</c>): statfs yields the backing BSD device, IOKit yields its
    /// IOMedia object, and the registry parent chain leads to the owning IOUSBHostDevice.
    /// Returns null when any step fails; the caller treats unknown ownership
    /// as acceptable rather than rejecting a working volume.
    /// </summary>
    public static (ushort VendorId, ushort ProductId, string DevicePath)? FindVolumeOwner(string mountPath)
    {
        try
        {
            var buf = new StatfsBuf();
            if (Statfs(mountPath, ref buf) != 0)
                return null;
            int len = Array.IndexOf(buf.f_mntfromname, (byte)0);
            string mntFrom = System.Text.Encoding.UTF8.GetString(buf.f_mntfromname, 0, len < 0 ? buf.f_mntfromname.Length : len);
            if (!mntFrom.StartsWith("/dev/", StringComparison.Ordinal))
                return null;
            string bsdName = mntFrom["/dev/".Length..];

            // IOServiceGetMatchingService consumes the matching dictionary.
            IntPtr matching = IOBSDNameMatching(KIOMainPortDefault, 0, bsdName);
            if (matching == IntPtr.Zero)
                return null;
            uint entry = IOServiceGetMatchingService(KIOMainPortDefault, matching);

            // Walk the IOService plane upward from the IOMedia object to the USB device node.
            for (int depth = 0; entry != 0 && depth < 16; depth++)
            {
                try
                {
                    if (IOObjectConformsTo(entry, "IOUSBHostDevice"))
                    {
                        return (ReadUShortProperty(entry, "idVendor"),
                                ReadUShortProperty(entry, "idProduct"),
                                RegistryPath(entry));
                    }
                    if (IORegistryEntryGetParentEntry(entry, KIOServicePlane, out uint parent) != 0)
                        return null;
                    _ = IOObjectRelease(entry);
                    entry = parent;
                }
                catch
                {
                    _ = IOObjectRelease(entry);
                    throw;
                }
            }
            if (entry != 0)
                _ = IOObjectRelease(entry);
        }
        catch (Exception)
        {
            // Ownership resolution must never break the volume probe.
        }
        return null;
    }

    /// <summary>
    /// Enumerates the callout devices (<c>/dev/cu.*</c>) of the serial ports owned by the
    /// device with the given VID/PID, in IOKit registry order: each IOSerialBSDClient names
    /// its callout device, and the nearest ancestor holding idVendor/idProduct is the owning
    /// device. Callout devices are used because the dial-in tty.* twins block until carrier
    /// detect. Built-in ports such as tty.debug-console have no USB ancestor and never match.
    /// </summary>
    internal static IReadOnlyList<string> EnumerateCalloutDevices(ushort vendorId, ushort productId)
    {
        List<string> ports = [];
        if (IOServiceGetMatchingServices(KIOMainPortDefault, IOServiceMatching("IOSerialBSDClient"), out uint iterator) != 0)
            return ports;
        try
        {
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            {
                try
                {
                    if (ReadStringProperty(service, "IOCalloutDevice") is { } callout
                        && UsbAncestorMatches(service, vendorId, productId))
                    {
                        ports.Add(callout);
                    }
                }
                finally
                {
                    _ = IOObjectRelease(service);
                }
            }
        }
        finally
        {
            _ = IOObjectRelease(iterator);
        }
        return ports;
    }

    private static bool UsbAncestorMatches(uint service, ushort vendorId, ushort productId)
    {
        uint current = service;
        // The caller owns the starting service; this method releases only the parents it obtains.
        bool releaseCurrent = false;
        try
        {
            for (int depth = 0; depth < 12; depth++)
            {
                if (IORegistryEntryGetParentEntry(current, KIOServicePlane, out uint parent) != 0)
                    return false;
                if (releaseCurrent)
                    _ = IOObjectRelease(current);
                current = parent;
                releaseCurrent = true;

                ushort? entryVid = ReadUShortPropertyOrNull(current, "idVendor");
                ushort? entryPid = ReadUShortPropertyOrNull(current, "idProduct");
                if (entryVid is null || entryPid is null)
                    continue;
                // The nearest attribute-bearing ancestor decides; the hub above it also
                // carries idVendor/idProduct and must not match.
                return entryVid == vendorId && entryPid == productId;
            }
            return false;
        }
        finally
        {
            if (releaseCurrent)
                _ = IOObjectRelease(current);
        }
    }

    private static string? ReadStringProperty(uint service, string key)
    {
        IntPtr cfKey = CFStringCreateWithCString(IntPtr.Zero, key, KCfStringEncodingUtf8);
        if (cfKey == IntPtr.Zero)
            return null;
        try
        {
            IntPtr value = IORegistryEntryCreateCFProperty(service, cfKey, IntPtr.Zero, 0);
            if (value == IntPtr.Zero)
                return null;
            try
            {
                byte[] buffer = new byte[256];
                if (!CFStringGetCString(value, buffer, buffer.Length, KCfStringEncodingUtf8))
                    return null;
                int len = Array.IndexOf(buffer, (byte)0);
                return System.Text.Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
            }
            finally
            {
                CFRelease(value);
            }
        }
        finally
        {
            CFRelease(cfKey);
        }
    }

    private static ushort ReadUShortProperty(uint service, string key) =>
        ReadUShortPropertyOrNull(service, key) ?? 0;

    /// <summary>Distinguishes an absent property from a zero value (ancestor walks stop at the first entry that has the properties).</summary>
    private static ushort? ReadUShortPropertyOrNull(uint service, string key)
    {
        IntPtr cfKey = CFStringCreateWithCString(IntPtr.Zero, key, KCfStringEncodingUtf8);
        if (cfKey == IntPtr.Zero)
            return null;
        try
        {
            IntPtr number = IORegistryEntryCreateCFProperty(service, cfKey, IntPtr.Zero, 0);
            if (number == IntPtr.Zero)
                return null;
            try
            {
                return CFNumberGetValue(number, KCfNumberIntType, out int value) ? (ushort)value : null;
            }
            finally
            {
                CFRelease(number);
            }
        }
        finally
        {
            CFRelease(cfKey);
        }
    }
}
