using QmkToolbox.Usb.Hid;

namespace QmkToolbox.Desktop.Services.Hid;

/// <summary>
/// Production probe over <see cref="UsbHidInterfaces"/>. HID interface discovery is
/// enumeration-based; the tracker polls.
/// </summary>
internal sealed class HidInterfaceProbe : IHidProbe
{
    // Open needs the full interface info behind a key; this map holds the latest enumeration.
    private readonly Dictionary<HidDeviceKey, HidInterfaceInfo> _lastSeen = [];

    public void Start() { }

    public IReadOnlyList<HidDeviceKey> EnumerateKeys()
    {
        _lastSeen.Clear();
        foreach (HidInterfaceInfo iface in UsbHidInterfaces.EnumerateHidInterfaces().Where(HidConsoleDevice.Match))
            _lastSeen[new HidDeviceKey(iface.DevicePath, iface.UsagePage, iface.Usage)] = iface;
        return [.. _lastSeen.Keys];
    }

    public BaseHidDevice? Open(HidDeviceKey key) =>
        _lastSeen.TryGetValue(key, out HidInterfaceInfo? iface) ? HidConsoleDevice.TryCreate(iface) : null;

    public void Dispose() { }
}
