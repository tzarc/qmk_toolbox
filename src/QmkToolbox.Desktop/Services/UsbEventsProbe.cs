#if !WINDOWS
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using Usb.Events;

namespace QmkToolbox.Desktop.Services;

// Used on macOS only. Windows and Linux have native probes; see WindowsUsbProbe/LinuxUsbProbe.

/// <summary>
/// Usb.Events subscription plumbing for the macOS probe: translates raw watcher callbacks into
/// arrival/removal payloads. The subclass owns the ID parsing, the arrival enrichment
/// (bcdDevice, mass-storage flag), and the native present-device sweep.
/// </summary>
internal abstract class UsbEventsProbe : IUsbProbe
{
    private UsbEventWatcher? _watcher;

    public event Action<UsbDeviceInfo>? Arrived;
    public event Action<UsbRemovalHint>? Removed;

    public StringComparison PathComparison => StringComparison.Ordinal;

    public void Start()
    {
        _watcher = new UsbEventWatcher();
        _watcher.UsbDeviceAdded += OnAdded;
        _watcher.UsbDeviceRemoved += OnRemoved;
    }

    public void Stop()
    {
        if (_watcher == null)
            return;
        _watcher.UsbDeviceAdded -= OnAdded;
        _watcher.UsbDeviceRemoved -= OnRemoved;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();

    public abstract IEnumerable<UsbDeviceInfo> EnumeratePresent();

    /// <summary>Parses a Usb.Events ID string in this platform's wire format.</summary>
    protected abstract bool TryParseId(string? s, out ushort value);

    /// <summary>
    /// Reads bcdDevice and the mass-storage flag for an arriving device. Usb.Events surfaces
    /// neither on any platform; bcdDevice feeds the QMK-revision-marker check
    /// (BootloaderFactory.GetDeviceType) and the mass-storage flag gates the marker-volume
    /// probe (FlashOrchestrator). Arrival-only by construction: removals carry a
    /// <see cref="UsbRemovalHint"/>, which has no fields that would require querying.
    /// </summary>
    protected abstract (ushort Revision, bool IsMassStorage) Enrich(ushort vid, ushort pid, string devicePath);

    private void OnAdded(object? sender, UsbDevice usbDevice)
    {
        if (ToDeviceInfo(usbDevice.VendorID, usbDevice.ProductID, usbDevice.Vendor,
                usbDevice.Product ?? usbDevice.DeviceName, usbDevice.DeviceSystemPath) is { } device)
        {
            Arrived?.Invoke(device);
        }
    }

    private void OnRemoved(object? sender, UsbDevice usbDevice)
    {
        string path = usbDevice.DeviceSystemPath ?? "";
        TryParseId(usbDevice.VendorID, out ushort vid);
        TryParseId(usbDevice.ProductID, out ushort pid);
        if (vid == 0 && pid == 0)
            UsbDeviceParser.TryParseHwId(path, out vid, out pid, out _);
        Removed?.Invoke(new UsbRemovalHint(path, vid, pid));
    }

    internal UsbDeviceInfo? ToDeviceInfo(
        string? vendorId, string? productId, string? vendor, string? product, string? deviceSystemPath)
    {
        string devicePath = deviceSystemPath ?? "";

        TryParseId(vendorId, out ushort vid);
        TryParseId(productId, out ushort pid);
        if (vid == 0 && pid == 0 && !UsbDeviceParser.TryParseHwId(devicePath, out vid, out pid, out _))
            return null;

        (ushort rev, bool isMassStorage) = Enrich(vid, pid, devicePath);
        return new UsbDeviceInfo(vid, pid, rev, vendor ?? "", product ?? "", "", devicePath, isMassStorage);
    }
}
#endif
