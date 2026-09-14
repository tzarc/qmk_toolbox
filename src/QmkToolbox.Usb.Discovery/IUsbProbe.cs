
namespace QmkToolbox.Usb.Discovery;

/// <summary>
/// The per-OS USB event source a <see cref="UsbDeviceTracker"/> runs over. An implementation
/// reports each arrival as a complete <see cref="UsbDeviceInfo"/> and each removal as a
/// <see cref="UsbRemovalHint"/>. Tracking devices, dropping duplicate arrivals, and matching
/// removals against them are the tracker's job, not the probe's.
/// </summary>
public interface IUsbProbe : IDisposable
{
    /// <summary>Raised for each device arrival, on the probe's own thread.</summary>
    event Action<UsbDeviceInfo> Arrived;

    /// <summary>
    /// Raised for each device removal, on the probe's own thread, with whatever identity the
    /// platform still reports.
    /// </summary>
    event Action<UsbRemovalHint> Removed;

    /// <summary>How device paths compare on this platform (Windows interface paths are case-insensitive).</summary>
    StringComparison PathComparison { get; }

    /// <summary>
    /// Enumerates the devices connected right now; the tracker calls this after
    /// <see cref="Start"/> to pick up devices attached before monitoring began.
    /// </summary>
    IEnumerable<UsbDeviceInfo> EnumeratePresent();

    /// <summary>
    /// Starts delivering events. Throws when the platform's notification mechanism cannot be
    /// set up; a silent failure would leave detection dead for the whole session.
    /// </summary>
    void Start();

    /// <summary>Stops delivering events; <see cref="Start"/> may be called again afterwards.</summary>
    void Stop();
}
