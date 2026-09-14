using System.Runtime.Versioning;

namespace QmkToolbox.Usb.Discovery.Windows;

/// <summary>
/// Resolves which USB device a mounted volume belongs to: drive letter → physical disk number →
/// disk devnode → cfgmgr32 parent chain up to the owning <c>USB\VID_…</c> device instance.
/// Returns null when any step fails; the caller treats unknown ownership as acceptable rather
/// than rejecting a working volume.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsVolumeOwner
{
    // GUID_DEVINTERFACE_DISK
    private static readonly Guid GuidDevInterfaceDisk = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");

    /// <summary>
    /// Returns the device instance ID (e.g. <c>USB\VID_2E8A&amp;PID_0003\serial</c>) of the USB
    /// device carrying the volume mounted at <paramref name="driveRoot"/> (e.g. <c>E:\</c>),
    /// or null when ownership cannot be determined.
    /// </summary>
    public static string? GetOwningUsbInstanceId(string driveRoot)
    {
        try
        {
            uint? diskNumber = Interop.Kernel32.GetDiskNumber($@"\\.\{driveRoot.TrimEnd('\\', '/')}");
            if (diskNumber is not { } number)
                return null;
            string? diskInstanceId = FindDiskInstanceByNumber(number);
            return diskInstanceId == null ? null : WalkUpToUsbInstance(diskInstanceId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? FindDiskInstanceByNumber(uint diskNumber)
    {
        foreach (string interfacePath in Interop.Cfgmgr32.GetDeviceInterfaceList(GuidDevInterfaceDisk))
        {
            if (Interop.Kernel32.GetDiskNumber(interfacePath) == diskNumber)
                return UsbDeviceParser.InterfacePathToInstanceId(interfacePath);
        }
        return null;
    }

    private static string? WalkUpToUsbInstance(string diskInstanceId)
    {
        if (Interop.Cfgmgr32.CM_Locate_DevNodeW(out uint node, diskInstanceId, 0) != Interop.Cfgmgr32.CR_SUCCESS)
            return null;
        // USBSTOR sits directly under the USB (or usbccgp function) node; a short walk is plenty.
        for (int depth = 0; depth < 8; depth++)
        {
            if (Interop.Cfgmgr32.CM_Get_Parent(out uint parent, node, 0) != Interop.Cfgmgr32.CR_SUCCESS)
                return null;
            node = parent;
            string id = Interop.Cfgmgr32.GetDeviceId(node);
            if (id.StartsWith(@"USB\VID_", StringComparison.OrdinalIgnoreCase))
            {
                // A composite function (USB\VID_…&MI_xx\…) belongs to its root device one level up,
                // which is what arrival events and the sweep track.
                if (id.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
                    continue;
                return id;
            }
        }
        return null;
    }
}
