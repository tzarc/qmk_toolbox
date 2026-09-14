namespace QmkToolbox.Usb.Discovery;

/// <summary>
/// An immutable snapshot of a connected USB device, as delivered by
/// <see cref="IUsbEventsDetector.DeviceConnected"/>.
/// </summary>
/// <param name="vendorId">USB vendor ID (<c>idVendor</c>).</param>
/// <param name="productId">USB product ID (<c>idProduct</c>).</param>
/// <param name="revisionBcd">Device revision (<c>bcdDevice</c>) in BCD format (e.g. <c>0x0200</c> = 2.00).</param>
/// <param name="manufacturerString">Manufacturer string descriptor, or empty when unreported.</param>
/// <param name="productString">Product string descriptor, or empty when unreported.</param>
/// <param name="devicePath">The platform's identifying path for the device.</param>
/// <param name="isMassStorage">True when the device exposes a USB mass-storage interface.</param>
/// <param name="drivers">Every driver bound to the device, or none when the platform does not report them.</param>
public sealed class UsbDeviceInfo(
    ushort vendorId,
    ushort productId,
    ushort revisionBcd,
    string manufacturerString,
    string productString,
    string devicePath,
    bool isMassStorage = false,
    IEnumerable<string>? drivers = null)
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

    /// <summary>
    /// Every driver bound to the device (e.g. <c>"WinUSB"</c>), one per function. Empty when the
    /// platform does not report drivers. Membership ignores case, as the platforms do.
    /// </summary>
    /// <remarks>
    /// This names which drivers are bound, not which function holds each one. Class drivers
    /// such as <c>HidUsb</c>, <c>USBSTOR</c> and <c>usbser</c> bind to every interface of their
    /// class, so an entry here proves the interface of that class has it. Generic drivers such
    /// as <c>WinUSB</c>, <c>libusbK</c> and <c>libusb0</c> are assigned to one nominated
    /// interface, so an entry here proves only that some function holds it.
    /// </remarks>
    public IReadOnlySet<string> Drivers { get; } =
        new HashSet<string>(drivers ?? [], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The platform's identifying path for the device: a Windows device interface path, a Linux
    /// sysfs syspath, or a macOS IORegistry path. Empty when the platform reports none.
    /// </summary>
    public string DevicePath { get; } = devicePath;

    /// <summary>True when the device exposes a USB mass-storage interface.</summary>
    public bool IsMassStorage { get; } = isMassStorage;

    /// <summary>Formats the device as <c>Manufacturer Product (VVVV:PPPP:RRRR)</c>.</summary>
    public override string ToString() =>
        $"{ManufacturerString} {ProductString} ({VendorId:X4}:{ProductId:X4}:{RevisionBcd:X4})".Trim();

    /// <summary>Formats the device's identity as <c>VID:XXXX PID:XXXX</c>.</summary>
    public string TraceVidPid => $"VID:{VendorId:X4} PID:{ProductId:X4}";

    /// <summary>Formats the device's identity and revision as <c>VID:XXXX PID:XXXX REV:XXXX</c>.</summary>
    public string TraceVidPidRev => $"{TraceVidPid} REV:{RevisionBcd:X4}";

    /// <summary>
    /// Formats <see cref="DevicePath"/> for a diagnostic trace line: quoted, or <c>(empty)</c>
    /// when the platform reports none.
    /// </summary>
    public string TracePath => DevicePath.Length == 0 ? "(empty)" : $"\"{DevicePath}\"";
}
