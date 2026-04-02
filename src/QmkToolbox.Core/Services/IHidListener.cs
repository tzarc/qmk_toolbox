using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

public interface IHidListener : IDisposable
{
    event Action<IHidDevice> HidDeviceConnected;
    event Action<IHidDevice> HidDeviceDisconnected;
    event Action<IHidDevice, string> ConsoleReportReceived;
    event Action<string> ErrorOccurred;
    void Start();
    void Stop();
}
