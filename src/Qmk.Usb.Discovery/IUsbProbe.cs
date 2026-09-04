
namespace Qmk.Usb.Discovery;

/// <summary>
/// A raw per-OS USB event source beneath <see cref="UsbDeviceTracker"/>. Arrivals carry a fully
/// enriched <see cref="UsbDeviceInfo"/> (the probe holds the OS handles enrichment needs);
/// removals carry only a <see cref="UsbRemovalHint"/>; <see cref="EnumeratePresent"/> feeds the
/// tracker's startup sweep. Device tracking, arrival dedup, and removal matching are the
/// tracker's job, not the probe's.
/// </summary>
public interface IUsbProbe : IDisposable
{
    /// <summary>
    /// Raised for each device arrival, with a fully enriched payload. Raised on the probe's own
    /// thread.
    /// </summary>
    event Action<UsbDeviceInfo> Arrived;

    /// <summary>
    /// Raised for each device removal, with whatever identity the platform still reports.
    /// Raised on the probe's own thread.
    /// </summary>
    event Action<UsbRemovalHint> Removed;

    /// <summary>How device paths compare on this platform (Windows interface paths are case-insensitive).</summary>
    StringComparison PathComparison { get; }

    /// <summary>
    /// The devices connected right now, so devices attached before monitoring started are still
    /// reported. Called by the tracker after <see cref="Start"/>.
    /// </summary>
    IEnumerable<UsbDeviceInfo> EnumeratePresent();

    /// <summary>
    /// Starts delivering events. Throws when the platform's notification mechanism cannot be
    /// set up; USB detection would be dead for the whole session, so the failure must surface.
    /// </summary>
    void Start();

    /// <summary>Stops delivering events; <see cref="Start"/> may be called again afterwards.</summary>
    void Stop();
}
