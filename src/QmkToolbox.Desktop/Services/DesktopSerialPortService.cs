using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Qmk.Usb.Discovery;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Cross-platform serial port service.
/// <list type="bullet">
///   <item>Linux: VID/PID match against sysfs tty devices</item>
///   <item>macOS: VID/PID match against IOKit serial services</item>
///   <item>Windows: registry lookup mapping VID/PID → COM port name</item>
/// </list>
/// </summary>
public class DesktopSerialPortService : ISerialPortService
{
    public string? FindSerialPort(UsbDeviceInfo device) =>
        OperatingSystem.IsLinux() ? FindBySysfsLinux(device) :
        OperatingSystem.IsMacOS() ? FindByIoKitMacOS(device) :
        OperatingSystem.IsWindows() ? FindByRegistryWindows(device) :
        null;

    /// <summary>
    /// Matches a USB device by VID/PID via sysfs: each class/tty entry resolves to the
    /// tty's device node, and the nearest ancestor holding idVendor/idProduct is the
    /// owning device. /dev/serial/by-id names cannot be used for this: udev builds
    /// them from string descriptors when the device has them (Caterina does), so VID/PID
    /// appears in the name only for descriptor-less devices.
    /// </summary>
    /// <param name="sysClassTty">Overrides the sysfs tty class directory (used by tests).</param>
    /// <param name="devDir">Overrides the device-node directory (used by tests).</param>
    internal static string? FindBySysfsLinux(UsbDeviceInfo device, string sysClassTty = "/sys/class/tty", string devDir = "/dev")
    {
        if (!Directory.Exists(sysClassTty))
            return null;

        foreach (string entry in Directory.EnumerateDirectories(sysClassTty))
        {
            try
            {
                // Walk up from the resolved class entry, not from the node's "device" link:
                // sysfs symlink targets are relative, and only the class entry sits under a
                // real directory where lexical resolution gives the true path.
                var entryInfo = new DirectoryInfo(entry);
                DirectoryInfo? dir = entryInfo.ResolveLinkTarget(returnFinalTarget: true) as DirectoryInfo ?? entryInfo;
                for (int depth = 0; dir is not null && depth < 8; dir = dir.Parent, depth++)
                {
                    string vidFile = Path.Combine(dir.FullName, "idVendor");
                    string pidFile = Path.Combine(dir.FullName, "idProduct");
                    if (!File.Exists(vidFile) || !File.Exists(pidFile))
                        continue;
                    if (ReadHexAttribute(vidFile) == device.VendorId && ReadHexAttribute(pidFile) == device.ProductId)
                        return Path.Combine(devDir, Path.GetFileName(entry));
                    // The nearest attribute-bearing ancestor is the owning device; the hub
                    // above it also carries idVendor/idProduct and must not match.
                    break;
                }
            }
            catch (IOException)
            {
                // The tty vanished mid-walk (unplug race); the poll retries.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    private static ushort? ReadHexAttribute(string path) =>
        ushort.TryParse(File.ReadAllText(path).Trim(), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out ushort value) ? value : null;

    /// <summary>
    /// Matches a USB device by VID/PID in the IOKit registry: each IOSerialBSDClient names
    /// its callout device, and the nearest ancestor holding idVendor/idProduct is the owning
    /// device. Returns the callout device (/dev/cu.*): the dial-in tty.* twin blocks until
    /// carrier detect. Built-in ports such as tty.debug-console have no USB ancestor and
    /// never match.
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static string? FindByIoKitMacOS(UsbDeviceInfo device)
    {
        if (IOServiceGetMatchingServices(IntPtr.Zero, IOServiceMatching("IOSerialBSDClient"), out IntPtr iterator) != 0)
            return null;
        try
        {
            IntPtr service;
            while ((service = IOIteratorNext(iterator)) != IntPtr.Zero)
            {
                try
                {
                    if (ReadCfStringProperty(service, "IOCalloutDevice") is { } callout
                        && UsbAncestorMatches(service, device.VendorId, device.ProductId))
                    {
                        return callout;
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
        return null;
    }

    [SupportedOSPlatform("macos")]
    private static bool UsbAncestorMatches(IntPtr service, ushort vid, ushort pid)
    {
        IntPtr current = service;
        // The caller owns the starting service; this method releases only the parents it obtains.
        bool releaseCurrent = false;
        try
        {
            for (int depth = 0; depth < 12; depth++)
            {
                if (IORegistryEntryGetParentEntry(current, "IOService", out IntPtr parent) != 0)
                    return false;
                if (releaseCurrent)
                    _ = IOObjectRelease(current);
                current = parent;
                releaseCurrent = true;

                ushort? entryVid = ReadCfUShortProperty(current, "idVendor");
                ushort? entryPid = ReadCfUShortProperty(current, "idProduct");
                if (entryVid is null || entryPid is null)
                    continue;
                // The nearest attribute-bearing ancestor decides; the hub above it also
                // carries idVendor/idProduct and must not match.
                return entryVid == vid && entryPid == pid;
            }
            return false;
        }
        finally
        {
            if (releaseCurrent)
                _ = IOObjectRelease(current);
        }
    }

    [SupportedOSPlatform("macos")]
    private static string? ReadCfStringProperty(IntPtr service, string key)
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

    [SupportedOSPlatform("macos")]
    private static ushort? ReadCfUShortProperty(IntPtr service, string key)
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

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int IORegistryEntryGetParentEntry(IntPtr entry, string plane, out IntPtr parent);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IORegistryEntryCreateCFProperty(IntPtr entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport(CoreFoundationLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, long bufferSize, uint encoding);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern bool CFNumberGetValue(IntPtr number, int theType, out int value);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern void CFRelease(IntPtr cf);

    /// <summary>
    /// Looks up the COM port assigned to a USB device by walking the Windows
    /// registry at HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_xxxx&amp;PID_xxxx.
    /// Each child key contains a "Device Parameters" sub-key with a "PortName"
    /// value (e.g. "COM12"). Returns null when the lookup fails; guessing an
    /// unrelated port would point avrdude/mdloader at whatever serial device
    /// happens to exist (modems, debug probes).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? FindByRegistryWindows(UsbDeviceInfo device)
    {
        try
        {
            string vidPid = $"VID_{device.VendorId:X4}&PID_{device.ProductId:X4}";
            string keyPath = $@"SYSTEM\CurrentControlSet\Enum\USB\{vidPid}";

            using RegistryKey? usbKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (usbKey is not null)
            {
                // Sub-keys are device instances, keyed by serial number.
                foreach (string instanceId in usbKey.GetSubKeyNames())
                {
                    using RegistryKey? instanceKey = usbKey.OpenSubKey(instanceId);
                    using RegistryKey? paramsKey = instanceKey?.OpenSubKey("Device Parameters");
                    if (paramsKey?.GetValue("PortName") is string portName)
                    {
                        // The registry keeps PortName values for unplugged devices; accept only ports present now.
                        string[] activePorts = SerialPort.GetPortNames();
                        if (Array.Exists(activePorts, p => p.Equals(portName, StringComparison.OrdinalIgnoreCase)))
                            return portName;
                    }
                }
            }
        }
        catch
        {
            // Registry access may fail due to permissions; report no port.
        }
        return null;
    }
}
