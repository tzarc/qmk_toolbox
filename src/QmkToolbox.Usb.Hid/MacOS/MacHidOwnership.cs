using System.Globalization;
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
    private const string PathPrefix = "DevSrvsID:";

    internal static bool IsOwnedBy(string hidDevicePath, string ownerRegistryPath)
    {
        if (!hidDevicePath.StartsWith(PathPrefix, StringComparison.Ordinal)
            || !ulong.TryParse(hidDevicePath[PathPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out ulong entryId))
        {
            return false;
        }

        // IOServiceGetMatchingService consumes the matching dictionary.
        uint current = Interop.IOKit.IOServiceGetMatchingService(
            Interop.IOKit.KIOMainPortDefault, Interop.IOKit.IORegistryEntryIDMatching(entryId));
        if (current == 0)
            return false;
        // The first handle came from the lookup and every parent adds one; all are released.
        try
        {
            for (int depth = 0; depth < 8; depth++)
            {
                if (Interop.IOKit.RegistryPath(current) == ownerRegistryPath)
                    return true;
                if (Interop.IOKit.IORegistryEntryGetParentEntry(current, Interop.IOKit.KIOServicePlane, out uint parent) != 0)
                    return false;
                _ = Interop.IOKit.IOObjectRelease(current);
                current = parent;
            }
            return false;
        }
        finally
        {
            _ = Interop.IOKit.IOObjectRelease(current);
        }
    }
}
