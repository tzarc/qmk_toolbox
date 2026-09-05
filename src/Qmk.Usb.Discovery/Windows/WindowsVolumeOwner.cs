using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Qmk.Usb.Discovery.Windows;

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

    private const int CR_SUCCESS = 0x00000000;
    private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    private const uint GENERIC_NONE = 0;
    private const uint FILE_SHARE_READ_WRITE = 0x00000001 | 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_NUMBER
    {
        public uint DeviceType;
        public uint DeviceNumber;
        public uint PartitionNumber;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize,
        out STORAGE_DEVICE_NUMBER outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_List_SizeW(
        out uint pulLen, ref Guid interfaceClassGuid, string? pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid, string? pDeviceID, char[] buffer, uint bufferLen, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(uint dnDevInst, char[] buffer, uint bufferLen, uint ulFlags);

    /// <summary>
    /// Returns the device instance ID (e.g. <c>USB\VID_2E8A&amp;PID_0003\serial</c>) of the USB
    /// device carrying the volume mounted at <paramref name="driveRoot"/> (e.g. <c>E:\</c>),
    /// or null when ownership cannot be determined.
    /// </summary>
    public static string? GetOwningUsbInstanceId(string driveRoot)
    {
        try
        {
            uint? diskNumber = GetDiskNumber($@"\\.\{driveRoot.TrimEnd('\\', '/')}");
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

    private static uint? GetDiskNumber(string devicePath)
    {
        using SafeFileHandle handle = CreateFileW(
            devicePath, GENERIC_NONE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        return handle.IsInvalid
            ? null
            : DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0,
            out STORAGE_DEVICE_NUMBER number, (uint)Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(), out _, IntPtr.Zero)
            ? number.DeviceNumber
            : null;
    }

    private static string? FindDiskInstanceByNumber(uint diskNumber)
    {
        Guid guid = GuidDevInterfaceDisk;
        if (CM_Get_Device_Interface_List_SizeW(out uint len, ref guid, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || len <= 1)
            return null;
        char[] buffer = new char[len];
        if (CM_Get_Device_Interface_ListW(ref guid, null, buffer, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
            return null;
        foreach (string interfacePath in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            using SafeFileHandle handle = CreateFileW(
                interfacePath, GENERIC_NONE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                continue;
            if (DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0,
                    out STORAGE_DEVICE_NUMBER number, (uint)Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(), out _, IntPtr.Zero) &&
                number.DeviceNumber == diskNumber)
            {
                return UsbDeviceParser.InterfacePathToInstanceId(interfacePath);
            }
        }
        return null;
    }

    private static string? WalkUpToUsbInstance(string diskInstanceId)
    {
        if (CM_Locate_DevNodeW(out uint node, diskInstanceId, 0) != CR_SUCCESS)
            return null;
        // USBSTOR sits directly under the USB (or usbccgp function) node; a short walk is plenty.
        for (int depth = 0; depth < 8; depth++)
        {
            if (CM_Get_Parent(out uint parent, node, 0) != CR_SUCCESS)
                return null;
            node = parent;
            string id = GetDeviceId(node);
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

    private static string GetDeviceId(uint devNode)
    {
        char[] buffer = new char[400]; // MAX_DEVICE_ID_LEN
        if (CM_Get_Device_IDW(devNode, buffer, (uint)buffer.Length, 0) != CR_SUCCESS)
            return "";
        int len = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, len < 0 ? buffer.Length : len);
    }
}
