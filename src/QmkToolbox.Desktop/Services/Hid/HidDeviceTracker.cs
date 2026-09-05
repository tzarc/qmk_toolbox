using QmkToolbox.Core.Models;

namespace QmkToolbox.Desktop.Services.Hid;

/// <summary>
/// Polls an <see cref="IHidProbe"/> for QMK console devices and raises hotplug and
/// console-report events. Events fire on the poll thread; subscribers marshal to the UI
/// thread themselves.
/// </summary>
public sealed class HidDeviceTracker(IHidProbe probe) : IHidListener
{
    public event Action<IHidDevice>? HidDeviceConnected;
    public event Action<IHidDevice>? HidDeviceDisconnected;
    public event Action<IHidDevice, string>? ConsoleReportReceived;
    public event Action<string>? ErrorOccurred;

    private readonly List<BaseHidDevice> _devices = [];
    private readonly Lock _devicesLock = new();
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public HidDeviceTracker() : this(new HidApiProbe()) { }

    public void Start()
    {
        probe.Start();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        // The poll loop runs until disposal; it must stay off the UI thread.
        _pollTask = Task.Run(async () =>
        {
            try
            {
                Poll();
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Poll cadence; hidapi has no hotplug callbacks.
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    Poll();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorOccurred?.Invoke($"HID polling stopped unexpectedly: {ex.Message}");
            }
        }, token);
    }

    // One enumerate-and-diff pass; internal so tests drive polls deterministically.
    internal void Poll()
    {
        HashSet<HidDeviceKey> currentKeys = [.. probe.EnumerateKeys()];
        List<BaseHidDevice> removed;
        List<BaseHidDevice> added = [];

        lock (_devicesLock)
        {
            removed = [.. _devices.Where(d => !currentKeys.Contains(KeyOf(d)))];
            foreach (BaseHidDevice device in removed)
                _devices.Remove(device);

            HashSet<HidDeviceKey> knownKeys = [.. _devices.Select(KeyOf)];
            foreach (HidDeviceKey key in currentKeys.Where(k => !knownKeys.Contains(k)))
            {
                BaseHidDevice? device = probe.Open(key);
                if (device == null)
                    continue;
                _devices.Add(device);
                device.ConsoleReportReceived += OnConsoleReport;
                added.Add(device);
            }
        }

        // Raise events outside the lock so a subscriber can never deadlock against it.
        foreach (BaseHidDevice device in removed)
        {
            device.ConsoleReportReceived -= OnConsoleReport;
            (device as IDisposable)?.Dispose();
            HidDeviceDisconnected?.Invoke(device);
        }
        foreach (BaseHidDevice device in added)
            HidDeviceConnected?.Invoke(device);
    }

    private static HidDeviceKey KeyOf(BaseHidDevice device) =>
        new(device.DevicePath, device.UsagePage, device.Usage);

    private void OnConsoleReport(BaseHidDevice device, string data) =>
        ConsoleReportReceived?.Invoke(device, data);

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _pollTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Poll loop ended via cancellation or fault; nothing left to wait on.
        }
        lock (_devicesLock)
        {
            foreach (BaseHidDevice device in _devices)
            {
                device.ConsoleReportReceived -= OnConsoleReport;
                (device as IDisposable)?.Dispose();
            }
            _devices.Clear();
        }
        probe.Dispose();
        _cts?.Dispose();
    }
}
