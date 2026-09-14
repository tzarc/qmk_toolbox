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
    internal static bool IsOwnedBy(string hidInterfacePath, string ownerDevicePath)
    {
        string ownerInstanceId = InterfacePathToInstanceId(ownerDevicePath);
        if (Interop.Cfgmgr32.CM_Locate_DevNodeW(out uint node, InterfacePathToInstanceId(hidInterfacePath), 0) != Interop.Cfgmgr32.CR_SUCCESS)
            return false;
        for (int depth = 0; depth < 8; depth++)
        {
            if (Interop.Cfgmgr32.CM_Get_Parent(out uint parent, node, 0) != Interop.Cfgmgr32.CR_SUCCESS)
                return false;
            node = parent;
            string id = Interop.Cfgmgr32.GetDeviceId(node);
            // A composite function (USB\VID_…&MI_xx\…) belongs to its root device further up,
            // which is the device that arrival events and the startup sweep report.
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
}
