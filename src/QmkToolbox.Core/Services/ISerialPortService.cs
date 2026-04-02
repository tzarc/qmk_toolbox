using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

public interface ISerialPortService
{
    string? FindSerialPort(IUsbDevice device);
}
