
namespace QmkToolbox.Usb.Discovery;

/// <summary>
/// Monitors USB device arrival and removal events.
/// </summary>
public interface IUsbEventsDetector : IDisposable
{
    /// <summary>Raised when a USB device is connected.</summary>
    event Action<UsbDeviceInfo> DeviceConnected;

    /// <summary>
    /// Raised when a USB device is disconnected. Always delivers the same
    /// <see cref="UsbDeviceInfo"/> instance that <see cref="DeviceConnected"/> delivered, so
    /// subscribers can track devices by reference. A platform removal event that reports less
    /// than the arrival did must still resolve back to that instance.
    /// </summary>
    event Action<UsbDeviceInfo> DeviceDisconnected;

    /// <summary>Receives diagnostic trace lines for USB events. Called on the detector's own thread; marshal in your handler.</summary>
    Action<string>? DiagnosticTrace { get; set; }

    /// <summary>Starts monitoring for USB device events. Throws if the platform's notification mechanism cannot be set up.</summary>
    void Start();

    /// <summary>Stops monitoring for USB device events.</summary>
    void Stop();
}
