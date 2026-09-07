using System.Globalization;
using System.Runtime.Versioning;

using Microsoft.Win32;
using Qmk.Usb.Discovery.MacOS;

namespace Qmk.Usb.Discovery;

/// <summary>
/// Resolves the serial ports a USB device exposes, for example the port a CDC-ACM bootloader
/// must be flashed through.
/// </summary>
public static class UsbSerialPorts
{
    /// <summary>
    /// Enumerates the serial ports backed by <paramref name="device"/>: Linux device nodes
    /// (<c>/dev/ttyACM0</c>), macOS callout devices (<c>/dev/cu.usbmodem1101</c>), or Windows
    /// COM port names (<c>COM3</c>). A device with several serial interfaces yields them all,
    /// in a stable platform order; a device with none yields nothing.
    /// </summary>
    /// <param name="device">The device to resolve, as delivered by
    /// <see cref="IUsbEventsDetector.DeviceConnected"/>.</param>
    public static IEnumerable<string> EnumerateSerialPorts(this UsbDeviceInfo device) =>
        OperatingSystem.IsLinux() ? EnumerateSerialPortsLinux(device) :
        OperatingSystem.IsMacOS() ? MacUsbRegistry.EnumerateCalloutDevices(device.VendorId, device.ProductId) :
        OperatingSystem.IsWindows() ? EnumerateSerialPortsWindows(device) :
        [];

    /// <summary>
    /// Matches by VID/PID via sysfs: each class/tty entry resolves to the tty's device node,
    /// and the nearest ancestor holding idVendor/idProduct is the owning device.
    /// /dev/serial/by-id names cannot be used for this: udev builds them from string
    /// descriptors when the device has them (Caterina does), so VID/PID appears in the name
    /// only for descriptor-less devices. Entries are visited in node-name order, so a
    /// multi-port device yields its lower-numbered tty first.
    /// </summary>
    /// <param name="sysClassTty">Overrides the sysfs tty class directory (used by tests).</param>
    /// <param name="devDir">Overrides the device-node directory (used by tests).</param>
    internal static IEnumerable<string> EnumerateSerialPortsLinux(UsbDeviceInfo device, string sysClassTty = "/sys/class/tty", string devDir = "/dev")
    {
        if (!Directory.Exists(sysClassTty))
            yield break;

        foreach (string entry in Directory.EnumerateDirectories(sysClassTty).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            bool matched = false;
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
                    matched = ReadHexAttribute(vidFile) == device.VendorId && ReadHexAttribute(pidFile) == device.ProductId;
                    // The nearest attribute-bearing ancestor is the owning device; the hub
                    // above it also carries idVendor/idProduct and must not match.
                    break;
                }
            }
            catch (IOException)
            {
                // The tty vanished mid-walk (unplug race); callers typically poll and retry.
            }
            catch (UnauthorizedAccessException)
            {
            }
            if (matched)
                yield return Path.Combine(devDir, Path.GetFileName(entry));
        }
    }

    private static ushort? ReadHexAttribute(string path) =>
        ushort.TryParse(File.ReadAllText(path).Trim(), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out ushort value) ? value : null;

    /// <summary>
    /// Looks up the COM ports assigned to a USB device by walking the registry at
    /// HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_xxxx&amp;PID_xxxx. Each child key is a device
    /// instance whose "Device Parameters" sub-key holds a "PortName" value (e.g. "COM12").
    /// The registry keeps PortName values for unplugged devices, so only ports present in
    /// HARDWARE\DEVICEMAP\SERIALCOMM qualify; guessing would hand callers an unrelated serial
    /// device (modems, debug probes).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> EnumerateSerialPortsWindows(UsbDeviceInfo device)
    {
        List<string> ports = [];
        try
        {
            string vidPid = $"VID_{device.VendorId:X4}&PID_{device.ProductId:X4}";
            string keyPath = $@"SYSTEM\CurrentControlSet\Enum\USB\{vidPid}";

            using RegistryKey? usbKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (usbKey is not null)
            {
                HashSet<string> present = PresentComPorts();
                foreach (string instanceId in usbKey.GetSubKeyNames())
                {
                    using RegistryKey? instanceKey = usbKey.OpenSubKey(instanceId);
                    using RegistryKey? paramsKey = instanceKey?.OpenSubKey("Device Parameters");
                    if (paramsKey?.GetValue("PortName") is string portName && present.Contains(portName))
                        ports.Add(portName);
                }
            }
        }
        catch
        {
            // Registry access may fail due to permissions; report no ports.
        }
        return ports;
    }

    /// <summary>The COM ports present right now, from HARDWARE\DEVICEMAP\SERIALCOMM.</summary>
    [SupportedOSPlatform("windows")]
    private static HashSet<string> PresentComPorts()
    {
        HashSet<string> ports = new(StringComparer.OrdinalIgnoreCase);
        using RegistryKey? serialComm = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (serialComm is null)
            return ports;
        foreach (string valueName in serialComm.GetValueNames())
        {
            if (serialComm.GetValue(valueName) is string portName)
                ports.Add(portName);
        }
        return ports;
    }
}
