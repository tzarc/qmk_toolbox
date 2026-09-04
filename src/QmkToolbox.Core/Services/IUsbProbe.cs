using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

/// <summary>
/// A raw per-OS USB event source beneath <see cref="UsbDeviceTracker"/>. Arrivals carry a fully
/// enriched <see cref="UsbDeviceInfo"/> (the probe holds the OS handles enrichment needs);
/// removals carry only a <see cref="UsbRemovalHint"/>; <see cref="EnumeratePresent"/> feeds the
/// tracker's startup sweep. Device tracking, arrival dedup, and removal matching are the
/// tracker's job, not the probe's.
/// </summary>
public interface IUsbProbe : IDisposable
{
    event Action<UsbDeviceInfo> Arrived;
    event Action<UsbRemovalHint> Removed;

    /// <summary>How device paths compare on this platform (Windows interface paths are case-insensitive).</summary>
    StringComparison PathComparison { get; }

    /// <summary>
    /// The devices connected right now, so a board already sitting in bootloader mode when the
    /// app launches is still detected. Called by the tracker after <see cref="Start"/>.
    /// </summary>
    IEnumerable<UsbDeviceInfo> EnumeratePresent();

    void Start();
    void Stop();
}
