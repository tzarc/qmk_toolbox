namespace Qmk.Usb.Discovery;

/// <summary>
/// The immutable snapshot of a connected USB device, as delivered by
/// <see cref="IUsbEventsDetector.DeviceConnected"/>.
/// </summary>
/// <param name="vendorId">USB vendor ID (<c>idVendor</c>).</param>
/// <param name="productId">USB product ID (<c>idProduct</c>).</param>
/// <param name="revisionBcd">Device revision (<c>bcdDevice</c>) in BCD format (e.g. <c>0x0200</c> = 2.00).</param>
/// <param name="manufacturerString">Manufacturer string descriptor, or empty when unreported.</param>
/// <param name="productString">Product string descriptor, or empty when unreported.</param>
/// <param name="driver">Driver or subsystem name reported by the OS, or empty when unreported.</param>
/// <param name="devicePath">The platform's identifying path for the device.</param>
/// <param name="isMassStorage">True when the device exposes a USB mass-storage interface.</param>
public sealed class UsbDeviceInfo(
    ushort vendorId,
    ushort productId,
    ushort revisionBcd,
    string manufacturerString,
    string productString,
    string driver,
    string devicePath,
    bool isMassStorage = false)
{
    /// <summary>USB vendor ID (<c>idVendor</c>).</summary>
    public ushort VendorId { get; } = vendorId;

    /// <summary>USB product ID (<c>idProduct</c>).</summary>
    public ushort ProductId { get; } = productId;

    /// <summary>Device revision (<c>bcdDevice</c>) in BCD format (e.g. <c>0x0200</c> = 2.00).</summary>
    public ushort RevisionBcd { get; } = revisionBcd;

    /// <summary>Manufacturer string descriptor, or empty when the device or OS reports none.</summary>
    public string ManufacturerString { get; } = manufacturerString;

    /// <summary>Product string descriptor, or empty when the device or OS reports none.</summary>
    public string ProductString { get; } = productString;

    /// <summary>Driver or subsystem name reported by the OS (e.g. <c>"WinUSB"</c>), or empty when unreported.</summary>
    public string Driver { get; } = driver;

    /// <summary>
    /// The platform's identifying path for the device: a Windows device interface path, a Linux
    /// sysfs syspath, or a macOS IORegistry path. Empty when the platform reports none.
    /// </summary>
    public string DevicePath { get; } = devicePath;

    /// <summary>True when the device exposes a USB mass-storage interface; populated on arrival only.</summary>
    public bool IsMassStorage { get; } = isMassStorage;

    /// <summary>Formats the device as <c>Manufacturer Product (VVVV:PPPP:RRRR)</c>.</summary>
    public override string ToString() =>
        $"{ManufacturerString} {ProductString} ({VendorId:X4}:{ProductId:X4}:{RevisionBcd:X4})".Trim();
}
