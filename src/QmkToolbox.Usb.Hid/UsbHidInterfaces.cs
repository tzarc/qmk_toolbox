using QmkToolbox.Usb.Discovery;
using QmkToolbox.Usb.Hid.Linux;
using QmkToolbox.Usb.Hid.MacOS;
using QmkToolbox.Usb.Hid.Windows;

namespace QmkToolbox.Usb.Hid;

/// <summary>
/// Enumerates the HID interfaces of connected devices and opens them for report I/O, for
/// example a keyboard's console interface.
/// </summary>
public static class UsbHidInterfaces
{
    // Process-lifetime hidapi initialisation; hid_exit is optional and never called.
    static UsbHidInterfaces()
    {
        HidApi.Hid.Init();
    }

    /// <summary>
    /// Enumerates every HID interface currently connected, one entry per top-level
    /// collection, so callers can select by usage page and usage. Exclusively held
    /// interfaces (regular keyboards, mice) still appear; they only refuse opening.
    /// </summary>
    public static IEnumerable<HidInterfaceInfo> EnumerateHidInterfaces() =>
        [.. HidApi.Hid.Enumerate().Select(d => new HidInterfaceInfo(
            d.VendorId, d.ProductId, d.ReleaseNumber,
            d.ManufacturerString ?? "", d.ProductString ?? "",
            d.UsagePage, d.Usage, d.Path))];

    /// <summary>
    /// Enumerates the HID interfaces backed by <paramref name="device"/>, one entry per
    /// top-level collection. The lookup anchors to the device instance, so two identical
    /// devices never see each other's interfaces.
    /// </summary>
    /// <param name="device">The device to resolve, as delivered by
    /// <see cref="IUsbEventsDetector.DeviceConnected"/>.</param>
    public static IEnumerable<HidInterfaceInfo> EnumerateHidInterfaces(this UsbDeviceInfo device) =>
        device.DevicePath.Length == 0
            ? []
            : EnumerateHidInterfaces().Where(iface => OwnedBy(iface, device));

    /// <summary>
    /// Opens the interface behind <paramref name="iface"/> for report I/O, or returns null
    /// when it vanished since enumeration or refuses to open.
    /// </summary>
    public static HidInterfaceDevice? Open(this HidInterfaceInfo iface) =>
        HidApiInterfaceDevice.TryOpen(iface.DevicePath);

    private static bool OwnedBy(HidInterfaceInfo iface, UsbDeviceInfo device) =>
        OperatingSystem.IsLinux() ? LinuxHidOwnership.IsOwnedBy(iface.DevicePath, device.DevicePath) :
        OperatingSystem.IsMacOS() ? MacHidOwnership.IsOwnedBy(iface.DevicePath, device.DevicePath) :
        OperatingSystem.IsWindows() && WindowsHidOwnership.IsOwnedBy(iface.DevicePath, device.DevicePath);
}
