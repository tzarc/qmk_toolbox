using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace QmkToolbox.Usb.Discovery.Windows;

/// <summary>
/// Resolves the COM ports of a USB device from its own device instance: the instance's
/// "Device Parameters" registry key holds the port name when a serial driver binds the device
/// directly, and a composite device's ports sit on its child functions, visited in interface
/// order. Anchoring to the instance keeps two identical boards from matching each other's
/// ports, which a VID/PID-wide registry scan cannot guarantee.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsSerialPorts
{
    private const int CR_SUCCESS = 0x00000000;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Child(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Sibling(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(uint dnDevInst, char[] buffer, uint bufferLen, uint ulFlags);

    internal static IEnumerable<string> EnumerateComPorts(UsbDeviceInfo device)
    {
        List<string> ports = [];
        if (device.DevicePath.Length == 0)
            return ports; // no identity to anchor to

        try
        {
            string instanceId = UsbDeviceParser.InterfacePathToInstanceId(device.DevicePath);
            if (CM_Locate_DevNodeW(out uint devNode, instanceId, 0) != CR_SUCCESS)
                return ports;

            HashSet<string> present = PresentComPorts();

            // A single-interface serial device carries the port on its own instance; a
            // composite device carries one per serial child function, in interface order.
            AddPortOf(instanceId, present, ports);
            if (CM_Get_Child(out uint child, devNode, 0) == CR_SUCCESS)
            {
                do
                {
                    AddPortOf(GetDeviceId(child), present, ports);
                }
                while (CM_Get_Sibling(out child, child, 0) == CR_SUCCESS);
            }
        }
        catch
        {
            // Registry access may fail due to permissions; report no ports.
        }
        return ports;
    }

    private static void AddPortOf(string instanceId, HashSet<string> present, List<string> ports)
    {
        if (instanceId.Length == 0)
            return;
        using RegistryKey? paramsKey =
            Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters");
        if (paramsKey?.GetValue("PortName") is string portName && present.Contains(portName))
            ports.Add(portName);
    }

    /// <summary>
    /// The COM ports present right now, from HARDWARE\DEVICEMAP\SERIALCOMM. The Enum tree
    /// keeps PortName values for unplugged devices, so membership here is what qualifies one.
    /// </summary>
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

    private static string GetDeviceId(uint devNode)
    {
        char[] buffer = new char[400]; // MAX_DEVICE_ID_LEN
        if (CM_Get_Device_IDW(devNode, buffer, (uint)buffer.Length, 0) != CR_SUCCESS)
            return "";
        int len = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, len < 0 ? buffer.Length : len);
    }
}
