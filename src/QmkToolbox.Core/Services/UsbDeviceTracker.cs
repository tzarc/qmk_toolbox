using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

/// <summary>
/// The platform-independent USB detection module: tracks devices delivered by an
/// <see cref="IUsbProbe"/>, deduplicates arrivals, sweeps already-present devices at
/// <see cref="Start"/>, and resolves lossy removals back to the tracked arrival, so that
/// <see cref="IUsbEventsDetector"/>'s identity invariant holds on every platform.
/// </summary>
public sealed class UsbDeviceTracker(IUsbProbe probe) : IUsbEventsDetector
{
    private readonly List<IUsbDevice> _devices = [];
    private readonly Lock _devicesLock = new();

    public event Action<IUsbDevice>? DeviceConnected;
    public event Action<IUsbDevice>? DeviceDisconnected;

    public Action<string>? DiagnosticTrace { get; set; }

    public void Start()
    {
        probe.Arrived += OnArrived;
        probe.Removed += OnRemoved;
        probe.Start();
        // The probe's live events cover future arrivals only; a board already sitting in
        // bootloader mode when the app launches must be swept up explicitly. The sweep runs
        // after subscription so nothing can slip between sweep and subscription: a device
        // delivered by both is dropped by OnArrived's duplicate-path guard.
        foreach (UsbDeviceInfo device in probe.EnumeratePresent())
            OnArrived(device);
    }

    public void Stop()
    {
        probe.Arrived -= OnArrived;
        probe.Removed -= OnRemoved;
        probe.Stop();
    }

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
        IUsbDevice? existing = null;
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
