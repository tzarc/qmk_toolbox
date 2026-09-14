namespace QmkToolbox.Usb.Discovery;

/// <summary>
/// The default <see cref="IUsbDeviceResolver"/>: the same results as the
/// <see cref="UsbSerialPorts"/> and <see cref="UsbVolumes"/> extension methods, for callers
/// that resolve through an injected interface.
/// </summary>
public sealed class UsbDeviceResolver : IUsbDeviceResolver
{
    /// <inheritdoc />
    public IEnumerable<string> EnumerateSerialPorts(UsbDeviceInfo device) =>
        device.EnumerateSerialPorts();

    /// <inheritdoc />
    public IEnumerable<string> EnumerateVolumes(UsbDeviceInfo device) =>
        device.EnumerateVolumes();
}
