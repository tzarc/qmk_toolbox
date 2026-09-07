using Xunit;

namespace Qmk.Usb.Discovery.Tests;

/// <summary>
/// Drives <see cref="UsbDeviceTracker"/> through the <see cref="IUsbProbe"/> seam: a fake probe
/// raises raw arrivals/removals and serves the startup sweep. The load-bearing assertions are
/// the identity invariant (removal delivers the arrival instance) and the sweep/dedup behaviour.
/// </summary>
public sealed class UsbDeviceTrackerTests : IDisposable
{
    private sealed class FakeProbe : IUsbProbe
    {
        public event Action<UsbDeviceInfo>? Arrived;
        public event Action<UsbRemovalHint>? Removed;

        public StringComparison PathComparison { get; set; } = StringComparison.Ordinal;
        public List<UsbDeviceInfo> Present { get; } = [];
        public bool Started;

        public IEnumerable<UsbDeviceInfo> EnumeratePresent() => Present;
        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Dispose() { }

        public void RaiseArrived(UsbDeviceInfo device) => Arrived?.Invoke(device);
        public void RaiseRemoved(UsbRemovalHint hint) => Removed?.Invoke(hint);
    }

    private static UsbDeviceInfo Device(ushort vid = 0x03EB, ushort pid = 0x2FF4, string path = "/sys/dev/1-2") =>
        new(vid, pid, 0, "QMK", "Board", "", path);

    private readonly FakeProbe _probe = new();
    private readonly List<UsbDeviceInfo> _connected = [];
    private readonly List<UsbDeviceInfo> _disconnected = [];
    private readonly UsbDeviceTracker _tracker;

    public UsbDeviceTrackerTests()
    {
        _tracker = new UsbDeviceTracker(_probe);
        _tracker.DeviceConnected += _connected.Add;
        _tracker.DeviceDisconnected += _disconnected.Add;
        _tracker.Start();
    }

    // ── arrivals ──────────────────────────────────────────────────────────────

    [Fact]
    public void Arrival_RaisesDeviceConnectedWithSameInstance()
    {
        UsbDeviceInfo device = Device();

        _probe.RaiseArrived(device);

        Assert.Same(device, Assert.Single(_connected));
    }

    [Fact]
    public void DuplicateArrival_SamePath_Ignored()
    {
        _probe.RaiseArrived(Device());
        _probe.RaiseArrived(Device());

        Assert.Single(_connected);
    }

    // ── removals: the identity invariant ──────────────────────────────────────

    [Fact]
    public void Removal_MatchedByPath_DeliversArrivalInstance()
    {
        UsbDeviceInfo device = Device();
        _probe.RaiseArrived(device);

        _probe.RaiseRemoved(new UsbRemovalHint(device.DevicePath));

        Assert.Same(device, Assert.Single(_disconnected));
    }

    [Fact]
    public void Removal_NoPath_FallsBackToVidPid()
    {
        UsbDeviceInfo other = Device(vid: 0x1111, pid: 0x2222, path: "/sys/dev/1-1");
        UsbDeviceInfo device = Device(path: "/sys/dev/1-2");
        _probe.RaiseArrived(other);
        _probe.RaiseArrived(device);

        _probe.RaiseRemoved(new UsbRemovalHint("", device.VendorId, device.ProductId));

        Assert.Same(device, Assert.Single(_disconnected));
    }

    [Fact]
    public void Removal_PathPreferredOverVidPid_ForIdenticalBoards()
    {
        // Two identical boards: only the path tells them apart.
        UsbDeviceInfo left = Device(path: "/sys/dev/1-1");
        UsbDeviceInfo right = Device(path: "/sys/dev/1-2");
        _probe.RaiseArrived(left);
        _probe.RaiseArrived(right);

        _probe.RaiseRemoved(new UsbRemovalHint(right.DevicePath, right.VendorId, right.ProductId));

        Assert.Same(right, Assert.Single(_disconnected));
    }

    [Fact]
    public void Removal_PathComparisonFollowsProbe()
    {
        _probe.PathComparison = StringComparison.OrdinalIgnoreCase;
        UsbDeviceInfo device = Device(path: @"\\?\USB#VID_03EB&PID_2FF4#Instance");
        _probe.RaiseArrived(device);

        _probe.RaiseRemoved(new UsbRemovalHint(device.DevicePath.ToUpperInvariant()));

        Assert.Same(device, Assert.Single(_disconnected));
    }

    [Fact]
    public void Removal_NothingMatches_DisconnectDropped()
    {
        _probe.RaiseArrived(Device());

        _probe.RaiseRemoved(new UsbRemovalHint("/sys/dev/9-9", 0x9999, 0x9999));

        Assert.Empty(_disconnected);
    }

    // ── startup sweep ─────────────────────────────────────────────────────────

    [Fact]
    public void Start_SweepsAlreadyPresentDevices()
    {
        var probe = new FakeProbe();
        UsbDeviceInfo present = Device();
        probe.Present.Add(present);
        var tracker = new UsbDeviceTracker(probe);
        var connected = new List<UsbDeviceInfo>();
        tracker.DeviceConnected += connected.Add;

        tracker.Start();

        Assert.True(probe.Started);
        Assert.Same(present, Assert.Single(connected));
    }

    [Fact]
    public void Start_DeviceDeliveredBySweepAndEvent_TrackedOnce()
    {
        var probe = new FakeProbe();
        UsbDeviceInfo present = Device();
        probe.Present.Add(present);
        var tracker = new UsbDeviceTracker(probe);
        var connected = new List<UsbDeviceInfo>();
        tracker.DeviceConnected += connected.Add;

        tracker.Start();
        probe.RaiseArrived(Device()); // the watcher reports the same device the sweep found

        Assert.Single(connected);
    }

    // ── lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public void StopThenStart_ReportsPresentDevicesAgain()
    {
        var probe = new FakeProbe();
        probe.Present.Add(Device());
        var tracker = new UsbDeviceTracker(probe);
        var connected = new List<UsbDeviceInfo>();
        tracker.DeviceConnected += connected.Add;
        tracker.Start();
        tracker.Stop();

        tracker.Start();

        Assert.Equal(2, connected.Count);
    }


    [Fact]
    public void Stop_UnsubscribesFromProbe()
    {
        _tracker.Stop();

        _probe.RaiseArrived(Device());

        Assert.False(_probe.Started);
        Assert.Empty(_connected);
    }

    public void Dispose() => _tracker.Dispose();
}
