using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

internal static partial class Interop
{
    /// <summary>Module and thread identity, and the raw device I/O that maps a volume to its physical disk.</summary>
    [SupportedOSPlatform("windows")]
    internal static class Kernel32
    {
        internal const uint GENERIC_NONE = 0;
        internal const uint FILE_SHARE_READ_WRITE = 0x00000001 | 0x00000002;
        internal const uint OPEN_EXISTING = 3;
        internal const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

        [StructLayout(LayoutKind.Sequential)]
        internal struct STORAGE_DEVICE_NUMBER
        {
            public uint DeviceType;
            public uint DeviceNumber;
            public uint PartitionNumber;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize,
            out STORAGE_DEVICE_NUMBER outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);

        /// <summary>
        /// The physical disk number behind a device path (a volume root such as <c>\\.\E:</c> or a
        /// disk interface path), or null when the path cannot be opened or queried.
        /// </summary>
        internal static uint? GetDiskNumber(string devicePath)
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
    }
}
