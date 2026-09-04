using HidApi;
using QmkToolbox.Core.Models;
using QmkToolbox.Desktop.Services.Hid;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Drives the tracker's diffing and device lifecycle through a fake probe. Polls are invoked
/// directly, so no timers or threads are involved and events fire synchronously.
/// </summary>
public sealed class HidDeviceTrackerTests
{
    private sealed class FakeConsoleDevice(string path, ushort usage) : BaseHidDevice(
        new DeviceInfo(path, 0xFEED, 0x0001, "", 0x0100, "QMK", "Board", 0xFF31, usage, 0, default)), IDisposable
    {
        public bool Disposed;
        public override bool IsConsoleDevice => true;
        public void EmitReport(string data) => RaiseConsoleReport(data);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProbe : IHidProbe
    {
        // A null value models a device that vanished between enumeration and open.
        public readonly Dictionary<HidDeviceKey, FakeConsoleDevice?> Present = [];
        public bool Disposed;

        public void Start() { }
        public IReadOnlyList<HidDeviceKey> EnumerateKeys() => [.. Present.Keys];
        public BaseHidDevice? Open(HidDeviceKey key) => Present.GetValueOrDefault(key);
        public void Dispose() => Disposed = true;

        public FakeConsoleDevice Add(string path, ushort usage = 0x0074)
        {
            var device = new FakeConsoleDevice(path, usage);
            Present[new HidDeviceKey(path, 0xFF31, usage)] = device;
            return device;
        }

        public void Remove(string path, ushort usage = 0x0074) =>
            Present.Remove(new HidDeviceKey(path, 0xFF31, usage));
    }

    private readonly FakeProbe _probe = new();
    private readonly HidDeviceTracker _tracker;
    private readonly List<IHidDevice> _connected = [];
    private readonly List<IHidDevice> _disconnected = [];
    private readonly List<(IHidDevice Device, string Data)> _reports = [];

    public HidDeviceTrackerTests()
    {
        _tracker = new HidDeviceTracker(_probe);
        _tracker.HidDeviceConnected += _connected.Add;
        _tracker.HidDeviceDisconnected += _disconnected.Add;
        _tracker.ConsoleReportReceived += (d, data) => _reports.Add((d, data));
    }

    [Fact]
    public void NewDevice_RaisesConnectedOnce()
    {
        FakeConsoleDevice device = _probe.Add("/dev/hidraw0");

        _tracker.Poll();
        _tracker.Poll();

        Assert.Equal([device], _connected);
        Assert.Empty(_disconnected);
    }

    [Fact]
    public void RemovedDevice_RaisesDisconnected_AndDisposesIt()
    {
        FakeConsoleDevice device = _probe.Add("/dev/hidraw0");
        _tracker.Poll();

        _probe.Remove("/dev/hidraw0");
        _tracker.Poll();

        Assert.Equal([device], _disconnected);
        Assert.True(device.Disposed);
    }

    [Fact]
    public void Reports_AreForwardedWhileTracked_AndStopAfterRemoval()
    {
        FakeConsoleDevice device = _probe.Add("/dev/hidraw0");
        _tracker.Poll();

        device.EmitReport("dbg: hello\n");
        _probe.Remove("/dev/hidraw0");
        _tracker.Poll();
        device.EmitReport("dbg: after removal\n");

        Assert.Equal([(device, "dbg: hello\n")], _reports);
    }

    // Two collections behind one hidraw node (Linux multi-collection devices) are
    // independent devices to the tracker.
    [Fact]
    public void CollectionsSharingAPath_AreTrackedIndependently()
    {
        FakeConsoleDevice console = _probe.Add("/dev/hidraw0", usage: 0x0074);
        FakeConsoleDevice other = _probe.Add("/dev/hidraw0", usage: 0x0061);
        _tracker.Poll();

        _probe.Remove("/dev/hidraw0", usage: 0x0061);
        _tracker.Poll();

        Assert.Equal([other], _disconnected);
        Assert.False(console.Disposed);
    }

    // A device can vanish between enumeration and open; it is skipped and simply retried
    // on the next poll if it reappears.
    [Fact]
    public void DeviceVanishingBeforeOpen_IsSkippedWithoutEvents()
    {
        _probe.Present[new HidDeviceKey("/dev/hidraw9", 0xFF31, 0x0074)] = null;

        _tracker.Poll();

        Assert.Empty(_connected);
        Assert.Empty(_disconnected);
    }

    [Fact]
    public void Dispose_DisposesTrackedDevicesAndProbe()
    {
        FakeConsoleDevice device = _probe.Add("/dev/hidraw0");
        _tracker.Poll();

        _tracker.Dispose();

        Assert.True(device.Disposed);
        Assert.True(_probe.Disposed);
    }
}
