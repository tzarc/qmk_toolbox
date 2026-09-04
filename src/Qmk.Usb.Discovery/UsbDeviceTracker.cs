
namespace Qmk.Usb.Discovery;

/// <summary>
/// Detects USB device arrivals and removals. Subscribe to <see cref="DeviceConnected"/> and
/// <see cref="DeviceDisconnected"/>, then call <see cref="Start"/>; devices already attached
/// are reported too. Removals always deliver the instance that arrived, so devices can be
/// tracked by reference. A custom <see cref="IUsbProbe"/> may replace the platform default.
/// </summary>
public sealed class UsbDeviceTracker(IUsbProbe probe) : IUsbEventsDetector
{
    /// <summary>Creates a tracker over the current platform's probe.</summary>
    public UsbDeviceTracker() : this(UsbProbe.CreateForCurrentPlatform()) { }

    private readonly List<UsbDeviceInfo> _devices = [];
    private readonly Lock _devicesLock = new();

    /// <inheritdoc />
    public event Action<UsbDeviceInfo>? DeviceConnected;

    /// <inheritdoc />
    public event Action<UsbDeviceInfo>? DeviceDisconnected;

    /// <inheritdoc />
    public Action<string>? DiagnosticTrace { get; set; }

    /// <summary>
    /// Starts monitoring. Devices already attached are delivered through
    /// <see cref="DeviceConnected"/> exactly once, then live events follow.
    /// </summary>
    public void Start()
    {
        probe.Arrived += OnArrived;
        probe.Removed += OnRemoved;
        probe.Start();
        // The probe's live events cover future arrivals only; devices attached before
        // monitoring started must be swept up explicitly. The sweep runs
        // after subscription so nothing can slip between sweep and subscription; a device
        // delivered by both is dropped by OnArrived's duplicate-path guard.
        foreach (UsbDeviceInfo device in probe.EnumeratePresent())
            OnArrived(device);
    }

    /// <summary>
    /// Stops monitoring and forgets the tracked devices; a later <see cref="Start"/> reports
    /// the devices present again.
    /// </summary>
    public void Stop()
    {
        probe.Arrived -= OnArrived;
        probe.Removed -= OnRemoved;
        probe.Stop();
        lock (_devicesLock)
        {
            _devices.Clear();
        }
    }

    /// <summary>Stops monitoring and disposes the probe.</summary>
    public void Dispose()
    {
        Stop();
        probe.Dispose();
    }

    private void OnArrived(UsbDeviceInfo device)
    {
        lock (_devicesLock)
        {
            if (device.DevicePath.Length > 0 &&
                _devices.Any(d => string.Equals(d.DevicePath, device.DevicePath, probe.PathComparison)))
            {
                DiagnosticTrace?.Invoke(
                    $"[USB+] duplicate arrival ignored {DeviceTrace.VidPidRev(device)} path:{DeviceTrace.Path(device.DevicePath)}");
                return;
            }
            _devices.Add(device);
        }
        DiagnosticTrace?.Invoke(
            $"[USB+] {DeviceTrace.VidPidRev(device)} path:{DeviceTrace.Path(device.DevicePath)}");
        DeviceConnected?.Invoke(device);
    }

    private void OnRemoved(UsbRemovalHint hint)
    {
        UsbDeviceInfo? existing = null;
        bool matchedByPath = false;
        int vidPidCandidates = 0;

        lock (_devicesLock)
        {
            if (hint.DevicePath.Length > 0)
            {
                existing = _devices.FirstOrDefault(d =>
                    d.DevicePath.Length > 0 && string.Equals(d.DevicePath, hint.DevicePath, probe.PathComparison));
                matchedByPath = existing != null;
            }

            // Removal events often drop the path, so fall back to VID/PID when the hint carries
            // one. A probe whose paths are canonical simply leaves the hint's VID/PID zero.
            if (existing == null && (hint.VendorId != 0 || hint.ProductId != 0))
            {
                if (DiagnosticTrace != null)
                    vidPidCandidates = _devices.Count(d => d.VendorId == hint.VendorId && d.ProductId == hint.ProductId);
                existing = _devices.FirstOrDefault(d => d.VendorId == hint.VendorId && d.ProductId == hint.ProductId);
            }

            if (existing != null)
                _devices.Remove(existing);
        }

        if (DiagnosticTrace != null)
        {
            DiagnosticTrace(
                $"[USB-] event path:{DeviceTrace.Path(hint.DevicePath)} VID:{hint.VendorId:X4} PID:{hint.ProductId:X4}");
            if (existing != null)
            {
                DiagnosticTrace(matchedByPath
                    ? $"[USB-] matched by path  ({DeviceTrace.VidPid(existing)})"
                    : $"[USB-] matched by VID/PID ({vidPidCandidates} candidate(s))  ({DeviceTrace.VidPid(existing)})");
            }
            else
            {
                DiagnosticTrace("[USB-] no match -> disconnect dropped");
            }
        }

        if (existing != null)
            DeviceDisconnected?.Invoke(existing);
    }
}
