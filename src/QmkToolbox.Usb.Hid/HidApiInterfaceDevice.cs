using HidApi;

namespace QmkToolbox.Usb.Hid;

/// <summary>
/// hidapi-backed report I/O: a pump thread turns hidapi's timeout reads into
/// <see cref="HidInterfaceDevice.ReportReceived"/> events. hidapi normalizes the read side
/// across platforms; the write side prepends the zero report-ID byte hidapi expects.
/// </summary>
internal sealed class HidApiInterfaceDevice : HidInterfaceDevice
{
    private readonly Device _device;
    private Thread? _pump;
    private volatile bool _disposed;
    private volatile bool _closedRaised;

    private HidApiInterfaceDevice(Device device)
    {
        _device = device;
    }

    internal static HidApiInterfaceDevice? TryOpen(string devicePath)
    {
        try
        {
            return new HidApiInterfaceDevice(new Device(devicePath));
        }
        catch (HidException)
        {
            return null;
        }
    }

    public override void Start()
    {
        _pump = new Thread(Pump) { IsBackground = true, Name = "HidApiReports" };
        _pump.Start();
    }

    private void Pump()
    {
        byte[] buffer = new byte[1024];
        try
        {
            // The read timeout bounds how long disposal waits for the pump to notice.
            while (!_disposed)
            {
                int bytes = _device.ReadTimeout(buffer, 100);
                if (bytes < 0)
                    break;
                if (bytes > 0)
                    RaiseReport(buffer[..bytes]);
            }
        }
        catch (Exception ex) when (ex is HidException or ObjectDisposedException)
        {
            // The interface is gone or the device was disposed; the pump ends here.
        }
        RaiseClosedOnce();
    }

    public override bool Write(ReadOnlySpan<byte> payload)
    {
        if (_disposed)
            return false;
        try
        {
            // hidapi writes expect the report-ID byte first; zero for interfaces without IDs.
            byte[] report = new byte[payload.Length + 1];
            payload.CopyTo(report.AsSpan(1));
            _device.Write(report);
            return true;
        }
        catch (HidException)
        {
            return false;
        }
    }

    private void RaiseClosedOnce()
    {
        if (!_closedRaised)
        {
            _closedRaised = true;
            RaiseClosed();
        }
    }

    public override void Dispose()
    {
        _disposed = true;
        if (_pump is { } pump && pump != Thread.CurrentThread)
            pump.Join(TimeSpan.FromSeconds(2));
        _device.Dispose();
        RaiseClosedOnce();
    }
}
