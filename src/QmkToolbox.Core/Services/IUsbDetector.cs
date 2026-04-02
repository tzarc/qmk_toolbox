using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

public interface IUsbDetector : IDisposable
{
    event Action<IUsbDevice> DeviceConnected;
    event Action<IUsbDevice> DeviceDisconnected;
    void Start();
    void Stop();
}
