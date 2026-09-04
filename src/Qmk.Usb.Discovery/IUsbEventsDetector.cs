
namespace Qmk.Usb.Discovery;

/// <summary>
/// Monitors USB device arrival and removal events.
/// </summary>
public interface IUsbEventsDetector : IDisposable
{
    /// <summary>Raised when a USB device is connected.</summary>
    event Action<UsbDeviceInfo> DeviceConnected;

    /// <summary>
    /// Raised when a USB device is disconnected. Always delivers the identical
    /// <see cref="UsbDeviceInfo"/> instance previously delivered by <see cref="DeviceConnected"/>;
    /// consumers may track devices by reference; all lossy-removal resolution happens inside
    /// the detector.
    /// </summary>
    event Action<UsbDeviceInfo> DeviceDisconnected;

    /// <summary>When set, receives diagnostic trace messages for USB events. Called from the detector's own thread; marshal if needed.</summary>
    Action<string>? DiagnosticTrace { get; set; }

    /// <summary>Starts monitoring for USB device events.</summary>
    void Start();

    /// <summary>Stops monitoring for USB device events.</summary>
    void Stop();
}
