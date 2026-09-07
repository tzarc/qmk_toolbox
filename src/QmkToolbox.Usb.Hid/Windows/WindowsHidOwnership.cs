using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QmkToolbox.Usb.Hid.Windows;

/// <summary>
/// Resolves whether a HID interface belongs to a USB device by walking the interface's
/// devnode parents to the USB root instance. Anchoring to the instance keeps two identical
/// devices from seeing each other's interfaces.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsHidOwnership
{
    private const int CR_SUCCESS = 0x00000000;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(uint dnDevInst, char[] buffer, uint bufferLen, uint ulFlags);

    internal static bool IsOwnedBy(string hidInterfacePath, string ownerDevicePath)
    {
        string ownerInstanceId = InterfacePathToInstanceId(ownerDevicePath);
        if (CM_Locate_DevNodeW(out uint node, InterfacePathToInstanceId(hidInterfacePath), 0) != CR_SUCCESS)
            return false;
        for (int depth = 0; depth < 8; depth++)
        {
            if (CM_Get_Parent(out uint parent, node, 0) != CR_SUCCESS)
                return false;
            node = parent;
            string id = GetDeviceId(node);
            // A composite function (USB\VID_…&MI_xx\…) belongs to its root device further up,
            // which is what arrival events and the sweep track.
            if (id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase)
                && !id.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(id, ownerInstanceId, StringComparison.OrdinalIgnoreCase);
            }
        }
        return false;
    }

    /// <summary>
    /// Converts a device interface path (<c>\\?\HID#VID_…#…#{guid}</c>) to its device
    /// instance ID (<c>HID\VID_…\…</c>): strip the prefix, map '#' to '\', drop the
    /// interface-class GUID segment.
    /// </summary>
    private static string InterfacePathToInstanceId(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path[4..];
        path = path.Replace('#', '\\');
        int guidStart = path.LastIndexOf('\\');
        return guidStart > 0 && guidStart + 1 < path.Length && path[guidStart + 1] == '{'
            ? path[..guidStart]
            : path;
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
