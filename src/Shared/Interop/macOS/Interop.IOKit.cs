using System.Runtime.InteropServices;
using System.Runtime.Versioning;

internal static partial class Interop
{
    /// <summary>
    /// IOKit registry access: matching dictionaries, service iteration, entry properties and
    /// ancestry, and arrival/termination notifications.
    /// </summary>
    /// <remarks>
    /// Handle types follow the headers exactly. <c>io_object_t</c> and its aliases
    /// (<c>io_service_t</c>, <c>io_registry_entry_t</c>, <c>io_iterator_t</c>) resolve through
    /// <c>mach_port_t</c> to <c>unsigned int</c>: a 32-bit index into the task's port namespace,
    /// never pointer-sized. They are <see cref="uint"/> here, as is every <c>mainPort</c>
    /// parameter. Only genuine pointers (<c>CFDictionaryRef</c>, <c>CFStringRef</c>,
    /// <c>CFTypeRef</c>, <c>IONotificationPortRef</c>) are <see cref="IntPtr"/>.
    /// </remarks>
    [SupportedOSPlatform("macos")]
    internal static class IOKit
    {
        internal const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";

        /// <summary>kIOMainPortDefault: the default Mach port for IOKit lookups.</summary>
        internal const uint KIOMainPortDefault = 0;

        internal const string KIOServicePlane = "IOService";

        /// <summary>io_string_t is a fixed 512-byte buffer.</summary>
        private const int IOStringLength = 512;

        internal delegate void IOServiceMatchingCallback(IntPtr refCon, uint iterator);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern IntPtr IONotificationPortCreate(uint mainPort);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern void IONotificationPortDestroy(IntPtr port);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern IntPtr IONotificationPortGetRunLoopSource(IntPtr port);

        [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern IntPtr IOServiceMatching(string name);

        [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern IntPtr IOBSDNameMatching(uint mainPort, uint options, string bsdName);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern IntPtr IORegistryEntryIDMatching(ulong entryId);

        [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern int IOServiceAddMatchingNotification(
            IntPtr port, string notificationType, IntPtr matching,
            IOServiceMatchingCallback callback, IntPtr refCon, out uint iterator);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern uint IOServiceGetMatchingService(uint mainPort, IntPtr matching);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern uint IOIteratorNext(uint iterator);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern int IOObjectRelease(uint obj);

        [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern bool IOObjectConformsTo(uint obj, string className);

        [DllImport(IOKitLib, ExactSpelling = true)]
        internal static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

        [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern int IORegistryEntryGetParentEntry(uint entry, string plane, out uint parent);

        [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern int IORegistryEntryGetPath(uint entry, string plane, byte[] path);

        /// <summary>The entry's path in the IOService plane, or empty when it cannot be read.</summary>
        internal static string RegistryPath(uint entry)
        {
            byte[] buffer = new byte[IOStringLength];
            if (IORegistryEntryGetPath(entry, KIOServicePlane, buffer) != 0)
                return "";
            int len = Array.IndexOf(buffer, (byte)0);
            return System.Text.Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
        }
    }
}
