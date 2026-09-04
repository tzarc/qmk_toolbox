#if !WINDOWS
using System.Runtime.Versioning;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Desktop.Services;

/// <summary>
/// macOS probe: IOKit-decimal ID parsing, IOKit-registry enrichment, and an IOKit sweep of
/// already-present devices.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacUsbProbe : UsbEventsProbe
{
    protected override bool TryParseId(string? s, out ushort value) =>
        UsbDeviceParser.TryParseUsbId(s, isMacOS: true, out value);

    protected override (ushort Revision, bool IsMassStorage) Enrich(ushort vid, ushort pid, string devicePath) =>
        (MacUsbRegistry.ReadBcdDevice(vid, pid), MacUsbRegistry.HasMassStorageInterface(vid, pid));

    public override IEnumerable<UsbDeviceInfo> EnumeratePresent() => MacUsbRegistry.EnumeratePresentDevices();
}
#endif
