using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.Services.Hid;
using QmkToolbox.Desktop.ViewModels;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Drives HidConsoleViewModel through the IHidListener seam with a fake adapter. The ViewModel
/// runs with an immediate invoker, so event handling executes synchronously. Devices are keyed by
/// DevicePath, so two identical keyboards (same label) stay distinct.
/// </summary>
public class HidConsoleViewModelTests
{
    private const string AllDevices = "(All connected devices)";

    private sealed class FakeHidListener : IHidListener
    {
        public event Action<IHidDevice>? HidDeviceConnected;
        public event Action<IHidDevice>? HidDeviceDisconnected;
        public event Action<IHidDevice, string>? ConsoleReportReceived;
        public event Action<string>? ErrorOccurred;

        public void Start() { }
        public void Dispose() { }

        public void RaiseConnected(IHidDevice d) => HidDeviceConnected?.Invoke(d);
        public void RaiseDisconnected(IHidDevice d) => HidDeviceDisconnected?.Invoke(d);
        public void RaiseReport(IHidDevice d, string data) => ConsoleReportReceived?.Invoke(d, data);
        public void RaiseError(string message) => ErrorOccurred?.Invoke(message);
    }

    private sealed class FakeHidDevice(string label, bool isConsole = true, string? path = null) : IHidDevice
    {
        public ushort VendorId => 0xFEED;
        public ushort ProductId => 0x0001;
        public ushort RevisionBcd => 0x0100;
        public ushort UsagePage => 0xFF31;
        public ushort Usage => 0x0074;
        public string ManufacturerString => "QMK";
        public string ProductString => label;
        public string DevicePath => path ?? $"/dev/hidraw-{label}";
        public bool IsConsoleDevice => isConsole;
        public override string ToString() => label;
    }

    private static (HidConsoleViewModel Vm, FakeHidListener Listener) NewConsole()
    {
        var listener = new FakeHidListener();
        var vm = new HidConsoleViewModel(listener, f => f(), _ => Task.CompletedTask);
        return (vm, listener);
    }

    private static IEnumerable<string> Labels(HidConsoleViewModel vm) => vm.Devices.Select(d => d.Label);

    [Fact]
    public void ConsoleDeviceConnected_AddsEntryAndLogs()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();

        listener.RaiseConnected(new FakeHidDevice("Planck"));

        Assert.Equal([AllDevices, "Planck"], Labels(vm));
        Assert.Contains("HID console device connected: Planck", TerminalProjection.ToText(vm.Buffer));
    }

    [Fact]
    public void NonConsoleDevice_Ignored()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();

        listener.RaiseConnected(new FakeHidDevice("Mouse", isConsole: false));

        Assert.Equal([AllDevices], Labels(vm));
        Assert.Equal("", TerminalProjection.ToText(vm.Buffer));
    }

    [Fact]
    public void IdenticalDevices_TrackedSeparatelyByPath()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();
        var left = new FakeHidDevice("Planck", path: "/dev/hidraw0");
        var right = new FakeHidDevice("Planck", path: "/dev/hidraw1");

        listener.RaiseConnected(left);
        listener.RaiseConnected(right);

        Assert.Equal([AllDevices, "Planck", "Planck"], Labels(vm));

        listener.RaiseDisconnected(left);

        Assert.Equal([AllDevices, "Planck"], Labels(vm));
        Assert.Equal("/dev/hidraw1", vm.Devices[1].DevicePath);
    }

    [Fact]
    public void DeviceDisconnected_RemovesEntry_AndResetsSelectionIfSelected()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();
        var device = new FakeHidDevice("Planck");
        listener.RaiseConnected(device);
        vm.SelectedDevice = vm.Devices[1];

        listener.RaiseDisconnected(device);

        Assert.Equal([AllDevices], Labels(vm));
        Assert.Equal(AllDevices, vm.SelectedDevice?.Label);
    }

    // The ComboBox clears its selection when the selected item is removed, and that null
    // arrives through the two-way binding after this handler runs; the selection must
    // already be off the entry before it leaves the collection.
    [Fact]
    public void DeviceDisconnected_SelectionResetsBeforeTheEntryIsRemoved()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();
        var device = new FakeHidDevice("Planck");
        listener.RaiseConnected(device);
        vm.SelectedDevice = vm.Devices[1];
        HidDeviceEntry? selectionAtRemoval = null;
        vm.Devices.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                selectionAtRemoval = vm.SelectedDevice;
        };

        listener.RaiseDisconnected(device);

        Assert.Equal(AllDevices, selectionAtRemoval?.Label);
    }

    [Fact]
    public void DeviceDisconnected_OtherDeviceSelected_SelectionKept()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();
        var planck = new FakeHidDevice("Planck");
        var corne = new FakeHidDevice("Corne");
        listener.RaiseConnected(planck);
        listener.RaiseConnected(corne);
        vm.SelectedDevice = vm.Devices.First(d => d.Label == "Corne");

        listener.RaiseDisconnected(planck);

        Assert.Equal("Corne", vm.SelectedDevice?.Label);
    }

    [Fact]
    public void ConsoleReport_AllDevicesSelected_Logged()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();
        var device = new FakeHidDevice("Planck");
        listener.RaiseConnected(device);

        listener.RaiseReport(device, "dbg: hello\n");

        Assert.Contains("dbg: hello", TerminalProjection.ToText(vm.Buffer));
    }

    [Fact]
    public void ConsoleReport_OtherDeviceSelected_Filtered()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();
        var planck = new FakeHidDevice("Planck");
        var corne = new FakeHidDevice("Corne");
        listener.RaiseConnected(planck);
        listener.RaiseConnected(corne);
        vm.SelectedDevice = vm.Devices.First(d => d.Label == "Corne");

        listener.RaiseReport(planck, "from planck\n");
        listener.RaiseReport(corne, "from corne\n");

        Assert.DoesNotContain("from planck", TerminalProjection.ToText(vm.Buffer));
        Assert.Contains("from corne", TerminalProjection.ToText(vm.Buffer));
    }

    [Fact]
    public void ConsoleReport_IdenticalLabels_FiltersByPath()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();
        var left = new FakeHidDevice("Planck", path: "/dev/hidraw0");
        var right = new FakeHidDevice("Planck", path: "/dev/hidraw1");
        listener.RaiseConnected(left);
        listener.RaiseConnected(right);
        vm.SelectedDevice = vm.Devices.First(d => d.DevicePath == "/dev/hidraw1");

        listener.RaiseReport(left, "from left\n");
        listener.RaiseReport(right, "from right\n");

        Assert.DoesNotContain("from left", TerminalProjection.ToText(vm.Buffer));
        Assert.Contains("from right", TerminalProjection.ToText(vm.Buffer));
    }

    [Fact]
    public void Error_Logged()
    {
        (HidConsoleViewModel vm, FakeHidListener listener) = NewConsole();

        listener.RaiseError("HID polling stopped unexpectedly: boom");

        Assert.Contains("HID polling stopped unexpectedly: boom", TerminalProjection.ToText(vm.Buffer));
    }

}
