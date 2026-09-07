using System.Globalization;
using System.Text.RegularExpressions;

namespace Qmk.Usb.Discovery;

/// <summary>USB device path and ID parsing helpers.</summary>
internal static class UsbDeviceParser
{
    private static readonly Regex HwIdRegex = new(
        @"VID_([0-9A-Fa-f]{4})&PID_([0-9A-Fa-f]{4})(?:&REV_([0-9A-Fa-f]{4}))?",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses a USB ID string as hex: bare four-digit (Windows instance IDs, sysfs
    /// attributes) or "0x"-prefixed (udev properties).
    /// </summary>
    public static bool TryParseUsbId(string? s, out ushort value)
    {
        value = 0;
        return !string.IsNullOrEmpty(s) && (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ushort.TryParse(s.AsSpan(2), NumberStyles.HexNumber, null, out value)
            : ushort.TryParse(s, NumberStyles.HexNumber, null, out value));
    }

    /// <summary>
    /// Extracts VID, PID, and (when present) revision from a Windows-format hardware ID or
    /// device path: any string carrying <c>VID_xxxx&amp;PID_xxxx[&amp;REV_xxxx]</c>.
    /// </summary>
    public static bool TryParseHwId(string devicePath, out ushort vid, out ushort pid, out ushort rev)
    {
        vid = pid = rev = 0;
        Match m = HwIdRegex.Match(devicePath);
        if (!m.Success)
            return false;
        vid = ushort.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        pid = ushort.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (m.Groups[3].Success)
            rev = ushort.Parse(m.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// Scans a hardware-ID list (Windows <c>CM_DRP_HARDWAREID</c> multi-sz, e.g.
    /// <c>USB\VID_03EB&amp;PID_2FF4&amp;REV_0936</c>, <c>USB\VID_03EB&amp;PID_2FF4</c>)
    /// for the first entry carrying a non-zero <c>REV_</c> value. Device instance IDs never
    /// contain <c>REV_</c>; only hardware IDs do.
    /// </summary>
    public static bool TryParseRevisionFromHardwareIds(IEnumerable<string> hardwareIds, out ushort rev)
    {
        foreach (string id in hardwareIds)
        {
            if (TryParseHwId(id, out _, out _, out rev) && rev != 0)
                return true;
        }
        rev = 0;
        return false;
    }

    /// <summary>
    /// Converts a Windows device interface path to the device instance ID used by cfgmgr32:
    /// <c>\\?\USB#VID_0483&amp;PID_DF11#serial#{guid}</c> → <c>USB\VID_0483&amp;PID_DF11\serial</c>.
    /// </summary>
    public static string InterfacePathToInstanceId(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            path = path[4..];
        }
        path = path.Replace('#', '\\');
        // Strip the trailing \{interface-class-guid} segment.
        int guidStart = path.LastIndexOf('\\');
        if (guidStart > 0 && guidStart + 1 < path.Length && path[guidStart + 1] == '{')
        {
            path = path[..guidStart];
        }
        return path;
    }

    /// <summary>
    /// Parses a Linux sysfs <c>bcdDevice</c> attribute value: four hex digits with a trailing
    /// newline (e.g. <c>"0936\n"</c> for revision 9.36).
    /// </summary>
    public static bool TryParseBcdDevice(string? text, out ushort rev)
    {
        rev = 0;
        return !string.IsNullOrWhiteSpace(text) &&
               ushort.TryParse(text.Trim(), NumberStyles.HexNumber, null, out rev);
    }

}
