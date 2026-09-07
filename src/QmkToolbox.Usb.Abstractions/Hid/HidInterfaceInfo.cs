namespace QmkToolbox.Usb.Hid;

/// <summary>
/// One top-level collection of a connected device's HID interface: USB identity, the
/// collection's usage pair, and the platform path that opens the interface. An interface
/// carrying several top-level collections yields one entry per collection.
/// </summary>
/// <param name="VendorId">USB vendor ID.</param>
/// <param name="ProductId">USB product ID.</param>
/// <param name="RevisionBcd">Device revision (BCD), or zero when the platform does not report it.</param>
/// <param name="Manufacturer">Manufacturer string; empty when the device reports none.</param>
/// <param name="Product">Product string; empty when the device reports none.</param>
/// <param name="UsagePage">Usage page of this top-level collection.</param>
/// <param name="Usage">Usage of this top-level collection.</param>
/// <param name="DevicePath">Platform path that identifies and opens the interface.</param>
public sealed record HidInterfaceInfo(
    ushort VendorId,
    ushort ProductId,
    ushort RevisionBcd,
    string Manufacturer,
    string Product,
    ushort UsagePage,
    ushort Usage,
    string DevicePath);
