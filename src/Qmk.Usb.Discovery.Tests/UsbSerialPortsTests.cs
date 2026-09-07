using Xunit;

namespace Qmk.Usb.Discovery.Tests;

/// <summary>
/// Exercises the Linux sysfs tty lookup against a temp tree shaped like real sysfs:
/// class/tty entries are relative symlinks into a devices tree whose USB device
/// directory holds idVendor/idProduct. The symlinks must keep their relative targets;
/// a lookup that resolves them lexically lands outside the tree.
/// </summary>
public sealed class UsbSerialPortsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("sysfs-test-").FullName;

    private string ClassTty => Path.Combine(_root, "class", "tty");
    private string DevDir => Path.Combine(_root, "dev");

    public UsbSerialPortsTests()
    {
        Directory.CreateDirectory(ClassTty);
        Directory.CreateDirectory(DevDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static UsbDeviceInfo Device(ushort vid, ushort pid) =>
        new(vid, pid, 0x0100, "", "", "", "");

    /// <summary>
    /// Recreates the sysfs shape for a USB tty: devices/{usbName} holds the attributes,
    /// the tty node sits at devices/{usbName}/{usbName}:1.0/tty/{ttyName} with a relative
    /// "device" link to the interface, and class/tty/{ttyName} is a relative symlink to the node.
    /// </summary>
    private void AddUsbTty(string ttyName, string usbName, string vid, string pid, string? parentDir = null)
    {
        string deviceDir = Path.Combine(parentDir ?? Path.Combine(_root, "devices"), usbName);
        string interfaceDir = Path.Combine(deviceDir, usbName + ":1.0");
        string ttyNode = Path.Combine(interfaceDir, "tty", ttyName);
        Directory.CreateDirectory(ttyNode);
        File.WriteAllText(Path.Combine(deviceDir, "idVendor"), vid + "\n");
        File.WriteAllText(Path.Combine(deviceDir, "idProduct"), pid + "\n");
        Directory.CreateSymbolicLink(Path.Combine(ttyNode, "device"), Path.Combine("..", ".."));

        string relativeNode = Path.GetRelativePath(ClassTty, ttyNode);
        Directory.CreateSymbolicLink(Path.Combine(ClassTty, ttyName), relativeNode);
    }

    // The Caterina regression case: the device publishes string descriptors, so nothing
    // in any udev-derived name carries the VID/PID; only the sysfs attributes do.
    [FactOnLinux]
    public void StringDescriptorBootloader_ResolvesByVidPid()
    {
        AddUsbTty("ttyACM0", "3-3", "2341", "0036");

        Assert.Equal([Path.Combine(DevDir, "ttyACM0")],
            UsbSerialPorts.EnumerateSerialPortsLinux(Device(0x2341, 0x0036), ClassTty, DevDir));
    }

    [FactOnLinux]
    public void OtherDevicesVidPid_DoesNotMatch()
    {
        AddUsbTty("ttyACM0", "3-3", "feed", "6060");

        Assert.Empty(UsbSerialPorts.EnumerateSerialPortsLinux(Device(0x2341, 0x0043), ClassTty, DevDir));
    }

    [FactOnLinux]
    public void TwoPorts_PicksTheOneWithMatchingIds()
    {
        AddUsbTty("ttyACM0", "3-3", "feed", "6060");
        AddUsbTty("ttyACM1", "3-4", "2341", "0036");

        Assert.Equal([Path.Combine(DevDir, "ttyACM1")],
            UsbSerialPorts.EnumerateSerialPortsLinux(Device(0x2341, 0x0036), ClassTty, DevDir));
    }

    // Platform UARTs and virtual consoles resolve under a devices subtree with no
    // idVendor/idProduct ancestor.
    [FactOnLinux]
    public void NonUsbTty_IsSkipped()
    {
        string node = Path.Combine(_root, "devices", "platform", "serial8250", "tty", "ttyS0");
        Directory.CreateDirectory(node);
        Directory.CreateSymbolicLink(Path.Combine(ClassTty, "ttyS0"), Path.GetRelativePath(ClassTty, node));
        AddUsbTty("ttyACM0", "3-3", "2341", "0036");

        Assert.Equal([Path.Combine(DevDir, "ttyACM0")],
            UsbSerialPorts.EnumerateSerialPortsLinux(Device(0x2341, 0x0036), ClassTty, DevDir));
    }

    // The hub above the device also carries idVendor/idProduct; only the nearest
    // attribute-bearing ancestor may decide the match.
    [FactOnLinux]
    public void HubAncestorIds_DoNotMatch()
    {
        string hubDir = Path.Combine(_root, "devices", "usb3");
        Directory.CreateDirectory(hubDir);
        File.WriteAllText(Path.Combine(hubDir, "idVendor"), "1d6b\n");
        File.WriteAllText(Path.Combine(hubDir, "idProduct"), "0002\n");
        AddUsbTty("ttyACM0", "3-3", "feed", "6060", parentDir: hubDir);

        Assert.Empty(UsbSerialPorts.EnumerateSerialPortsLinux(Device(0x1D6B, 0x0002), ClassTty, DevDir));
    }

    [FactOnLinux]
    public void MultiPortDevice_YieldsAllPortsInNodeNameOrder()
    {
        AddUsbTty("ttyACM1", "3-3", "2341", "0036");
        AddUsbTty("ttyACM0", "3-3", "2341", "0036");

        Assert.Equal([Path.Combine(DevDir, "ttyACM0"), Path.Combine(DevDir, "ttyACM1")],
            UsbSerialPorts.EnumerateSerialPortsLinux(Device(0x2341, 0x0036), ClassTty, DevDir));
    }

    [Fact]
    public void MissingDirectory_YieldsNothing() =>
        Assert.Empty(UsbSerialPorts.EnumerateSerialPortsLinux(
            Device(0xFEED, 0x6060), Path.Combine(_root, "does-not-exist"), DevDir));
}
