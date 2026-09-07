using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QmkToolbox.Usb.Hid.MacOS;

/// <summary>
/// Resolves whether a HID interface belongs to a USB device by walking the interface's
/// IOKit registry ancestry: hidapi's macOS paths are registry entry IDs
/// (<c>DevSrvsID:n</c>), and an ancestor's registry path must equal the device's.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacHidOwnership
{
    private const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string PathPrefix = "DevSrvsID:";

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IORegistryEntryIDMatching(ulong entryId);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern uint IOServiceGetMatchingService(IntPtr mainPort, IntPtr matching);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int IORegistryEntryGetParentEntry(uint entry, string plane, out uint parent);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern int IORegistryEntryGetPath(uint entry, string plane, byte[] path);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern int IOObjectRelease(uint obj);

    internal static bool IsOwnedBy(string hidDevicePath, string ownerRegistryPath)
    {
        if (!hidDevicePath.StartsWith(PathPrefix, StringComparison.Ordinal)
            || !ulong.TryParse(hidDevicePath[PathPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out ulong entryId))
        {
            return false;
        }

        // IOServiceGetMatchingService consumes the matching dictionary.
        uint current = IOServiceGetMatchingService(IntPtr.Zero, IORegistryEntryIDMatching(entryId));
        if (current == 0)
            return false;
        // The first handle came from the lookup and every parent adds one; all are released.
        try
        {
            for (int depth = 0; depth < 8; depth++)
            {
                if (RegistryPath(current) == ownerRegistryPath)
                    return true;
                if (IORegistryEntryGetParentEntry(current, "IOService", out uint parent) != 0)
                    return false;
                _ = IOObjectRelease(current);
                current = parent;
            }
            return false;
        }
        finally
        {
            _ = IOObjectRelease(current);
        }
    }

    private static string RegistryPath(uint entry)
    {
        byte[] buffer = new byte[512]; // io_string_t
        if (IORegistryEntryGetPath(entry, "IOService", buffer) != 0)
            return "";
        int len = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, len < 0 ? buffer.Length : len);
    }
}
