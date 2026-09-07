using System.IO.Ports;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Qmk.Usb.Discovery;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// Cross-platform serial port service.
/// <list type="bullet">
///   <item>Linux: VID/PID match against sysfs tty devices</item>
///   <item>macOS: most recently created /dev/cu.* device node</item>
///   <item>Windows: registry lookup mapping VID/PID → COM port name</item>
/// </list>
/// </summary>
public class DesktopSerialPortService : ISerialPortService
{
    public string? FindSerialPort(UsbDeviceInfo device) =>
        OperatingSystem.IsLinux() ? FindBySysfsLinux(device) :
        OperatingSystem.IsMacOS() ? FindNewestSerialPortMacOS() :
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
    /// Returns the most recently created /dev/cu.* serial device.
    /// FindSerialPort runs immediately after USB device detection, so the target
    /// device is the newest serial port.
    /// <para>
    /// Known limitation: another serial device appearing between detection and this call
    /// could be selected. In practice this window is small and users rarely have two
    /// devices in bootloader mode simultaneously.
    /// </para>
    /// </summary>
    [SupportedOSPlatform("macos")]
    private static string? FindNewestSerialPortMacOS()
    {
        return SerialPort.GetPortNames()
            .Select(p => new FileInfo(p))
            .Where(fi => fi.Exists)
            .OrderByDescending(fi => fi.CreationTimeUtc)
            .Select(fi => fi.FullName)
            .FirstOrDefault();
    }

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
