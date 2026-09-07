using Qmk.Usb.Discovery;

namespace QmkToolbox.Core.Services;

/// <summary>Serial port service backed by <see cref="UsbSerialPorts"/>; a multi-port device resolves to its primary interface.</summary>
public sealed class SystemSerialPortService : ISerialPortService
{
    public string? FindSerialPort(UsbDeviceInfo device) => device.EnumerateSerialPorts().FirstOrDefault();
}
