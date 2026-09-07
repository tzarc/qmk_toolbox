using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Qmk.Usb.Discovery.MacOS;

/// <summary>
/// Reads USB device properties and topology from the macOS IOKit registry: arrival payloads
/// straight off a device's <c>io_service_t</c>, the mass-storage interface check,
/// present-device enumeration for the startup sweep, and volume→device ownership resolution.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacUsbRegistry
{
    private const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint KCfStringEncodingUtf8 = 0x08000100;
    private const int KCfNumberIntType = 9;

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr IOServiceMatching(string name);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern int IOServiceGetMatchingServices(IntPtr mainPort, IntPtr matching, out IntPtr iterator);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IOIteratorNext(IntPtr iterator);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern int IOObjectRelease(IntPtr obj);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IORegistryEntryCreateCFProperty(IntPtr entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport(CoreFoundationLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern bool CFNumberGetValue(IntPtr number, int theType, out int value);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, long bufferSize, uint encoding);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int IORegistryEntryGetPath(IntPtr entry, string plane, byte[] path);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr IOBSDNameMatching(IntPtr mainPort, uint options, string bsdName);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IOServiceGetMatchingService(IntPtr mainPort, IntPtr matching);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int IORegistryEntryGetParentEntry(IntPtr entry, string plane, out IntPtr parent);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern bool IOObjectConformsTo(IntPtr obj, string className);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern void CFRelease(IntPtr cf);

    // macOS statfs: the 64-bit-inode variant is the plain symbol on arm64 but carries the
    // $INODE64 suffix on x86_64; both RIDs build from the same source, so pick at runtime.
    [DllImport("libSystem", EntryPoint = "statfs", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int StatfsArm64(string path, ref StatfsBuf buf);

    [DllImport("libSystem", EntryPoint = "statfs$INODE64", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int StatfsX64(string path, ref StatfsBuf buf);

    private static int Statfs(string path, ref StatfsBuf buf) =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? StatfsArm64(path, ref buf)
            : StatfsX64(path, ref buf);

    [StructLayout(LayoutKind.Sequential)]
    private struct StatfsBuf
    {
        public uint f_bsize;
        public int f_iosize;
        public ulong f_blocks, f_bfree, f_bavail, f_files, f_ffree;
        public ulong f_fsid;
        public uint f_owner;
        public uint f_type;
        public uint f_flags;
        public uint f_fssubtype;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] f_fstypename;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] f_mntonname;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] f_mntfromname;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] f_reserved;
    }

    /// <summary>
    /// Builds the arrival payload for a USB device service: identity, revision, strings, and
    /// registry path straight off the entry; the mass-storage flag via the interface query
    /// (with its settle window for late-registering interface nubs).
    /// </summary>
    internal static UsbDeviceInfo BuildDeviceInfo(IntPtr service)
    {
        ushort vid = ReadUShortProperty(service, "idVendor");
        ushort pid = ReadUShortProperty(service, "idProduct");
        return new UsbDeviceInfo(
            vid, pid,
            ReadUShortProperty(service, "bcdDevice"),
            ReadStringProperty(service, "USB Vendor Name") ?? "",
            ReadStringProperty(service, "USB Product Name") ?? "",
            "",
            RegistryPath(service),
            HasMassStorageInterface(vid, pid));
    }

    /// <summary>True for hubs (bDeviceClass 09) and entries without a usable VID/PID identity.</summary>
    internal static bool ShouldSkipDevice(IntPtr service)
    {
        ushort vid = ReadUShortProperty(service, "idVendor");
        ushort pid = ReadUShortProperty(service, "idProduct");
        return (vid == 0 && pid == 0) || ReadUShortProperty(service, "bDeviceClass") == 0x09;
    }

    /// <summary>Identity of a (possibly terminated) service, for removal matching.</summary>
    internal static (ushort VendorId, ushort ProductId, string DevicePath) ReadIdentity(IntPtr service) =>
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
        if (IOServiceGetMatchingServices(IntPtr.Zero, matching, out IntPtr iterator) != 0 || iterator == IntPtr.Zero)
            return false;
        try
        {
            IntPtr service;
            while ((service = IOIteratorNext(iterator)) != IntPtr.Zero)
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
            if (IOServiceGetMatchingServices(IntPtr.Zero, matching, out IntPtr iterator) != 0 || iterator == IntPtr.Zero)
                return devices;
            try
            {
                IntPtr service;
                while ((service = IOIteratorNext(iterator)) != IntPtr.Zero)
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
            IntPtr matching = IOBSDNameMatching(IntPtr.Zero, 0, bsdName);
            if (matching == IntPtr.Zero)
                return null;
            IntPtr entry = IOServiceGetMatchingService(IntPtr.Zero, matching);

            // Walk the IOService plane upward from the IOMedia object to the USB device node.
            for (int depth = 0; entry != IntPtr.Zero && depth < 16; depth++)
            {
                try
                {
                    if (IOObjectConformsTo(entry, "IOUSBHostDevice"))
                    {
                        return (ReadUShortProperty(entry, "idVendor"),
                                ReadUShortProperty(entry, "idProduct"),
                                RegistryPath(entry));
                    }
                    if (IORegistryEntryGetParentEntry(entry, "IOService", out IntPtr parent) != 0)
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
            if (entry != IntPtr.Zero)
                _ = IOObjectRelease(entry);
        }
        catch (Exception)
        {
            // Ownership resolution must never break the volume probe.
        }
        return null;
    }

    private static string RegistryPath(IntPtr service)
    {
        byte[] buffer = new byte[512]; // io_string_t
        if (IORegistryEntryGetPath(service, "IOService", buffer) != 0)
            return "";
        int len = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
    }

    private static string? ReadStringProperty(IntPtr service, string key)
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

    private static ushort ReadUShortProperty(IntPtr service, string key)
    {
        IntPtr cfKey = CFStringCreateWithCString(IntPtr.Zero, key, KCfStringEncodingUtf8);
        if (cfKey == IntPtr.Zero)
            return 0;
        try
        {
            IntPtr number = IORegistryEntryCreateCFProperty(service, cfKey, IntPtr.Zero, 0);
            if (number == IntPtr.Zero)
                return 0;
            try
            {
                return CFNumberGetValue(number, KCfNumberIntType, out int value) ? (ushort)value : (ushort)0;
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
