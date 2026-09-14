using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Tests;

/// <summary>
/// An <see cref="IUsbDeviceResolver"/> over two mutable lists, re-read on every call. Empty
/// lists mean "nothing attached yet", so a device whose flow never reaches a port or volume
/// needs no setup.
/// </summary>
internal sealed class FakeUsbResolver : IUsbDeviceResolver
{
    public List<string> SerialPorts { get; } = [];

    public List<string> Volumes { get; } = [];

    public IEnumerable<string> EnumerateSerialPorts(UsbDeviceInfo device) => SerialPorts;

    public IEnumerable<string> EnumerateVolumes(UsbDeviceInfo device) => Volumes;
}
