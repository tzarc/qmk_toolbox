using QmkToolbox.Usb.Discovery.Linux;
using Xunit;

namespace QmkToolbox.Usb.Discovery.Tests.Linux;

/// <summary>
/// Drives the Linux probe's three seams: the kernel-uevent datagram parser (raw byte fixtures
/// stand in for kernel traffic), the arrival build (uevent identity + sysfs strings against a
/// fixture-owned fake sysfs node), and the /sys/bus/usb startup sweep over a fake sysfs tree.
/// </summary>
public sealed class LinuxUsbProbeTests : IDisposable
{
    public void Dispose() => Directory.Delete(NodeDir, recursive: true);

    // ── uevent parsing ────────────────────────────────────────────────────────

    private static byte[] Datagram(params string[] segments) =>
        [.. segments.SelectMany(s => System.Text.Encoding.UTF8.GetBytes(s + "\0"))];

    [Fact]
    public void ParseUevent_UsbDeviceAdd_YieldsIdentityAndRevision()
    {
        // PRODUCT carries vid/pid/bcdDevice as unpadded hex; the revision must survive parsing.
        byte[] datagram = Datagram(
            "add@/devices/pci0000:00/usb3/3-1",
            "ACTION=add", "DEVPATH=/devices/pci0000:00/usb3/3-1",
            "SUBSYSTEM=usb", "DEVTYPE=usb_device", "PRODUCT=3eb/2ff4/936");

        LinuxUsbProbe.UsbUevent? parsed = LinuxUsbProbe.ParseUevent(datagram);

        Assert.NotNull(parsed);
        LinuxUsbProbe.UsbUevent uevent = parsed.Value;
        Assert.True(uevent.IsAdd);
        Assert.Equal("/devices/pci0000:00/usb3/3-1", uevent.DevPath);
        Assert.Equal(0x03EB, uevent.Vid);
        Assert.Equal(0x2FF4, uevent.Pid);
        Assert.Equal(0x0936, uevent.Rev);
    }

    [Fact]
    public void ParseUevent_UsbDeviceRemove_YieldsIdentity()
    {
        byte[] datagram = Datagram(
            "remove@/devices/pci0000:00/usb3/3-1",
            "ACTION=remove", "DEVPATH=/devices/pci0000:00/usb3/3-1",
            "SUBSYSTEM=usb", "DEVTYPE=usb_device", "PRODUCT=2e8a/3/100");

        LinuxUsbProbe.UsbUevent? parsed = LinuxUsbProbe.ParseUevent(datagram);

        Assert.NotNull(parsed);
        LinuxUsbProbe.UsbUevent uevent = parsed.Value;
        Assert.False(uevent.IsAdd);
        Assert.Equal(0x2E8A, uevent.Vid);
        Assert.Equal(0x0003, uevent.Pid);
    }

    [Theory]
    [InlineData("DEVTYPE=usb_interface")] // interface events would double every device
    [InlineData("SUBSYSTEM=block")]       // other subsystems are not USB arrivals
    [InlineData("ACTION=bind")]           // bind/unbind/change are not arrival/removal
    public void ParseUevent_NonDeviceEvents_Ignored(string overriding)
    {
        var segments = new Dictionary<string, string>
        {
            ["ACTION"] = "add",
            ["DEVPATH"] = "/devices/pci0000:00/usb3/3-1",
            ["SUBSYSTEM"] = "usb",
            ["DEVTYPE"] = "usb_device",
        };
        segments[overriding.Split('=')[0]] = overriding.Split('=')[1];
        byte[] datagram = Datagram([.. segments.Select(kv => $"{kv.Key}={kv.Value}")]);

        Assert.Null(LinuxUsbProbe.ParseUevent(datagram));
    }

    [Fact]
    public void ParseUevent_UdevdTaggedDatagram_Ignored()
    {
        // udevd's own multicast stream (group 2) carries a "libudev" magic header.
        byte[] datagram = Datagram("libudev", "ACTION=add", "SUBSYSTEM=usb", "DEVTYPE=usb_device", "DEVPATH=/x");

        Assert.Null(LinuxUsbProbe.ParseUevent(datagram));
    }

    // ── arrival build ─────────────────────────────────────────────────────────

    private LinuxUsbProbe.UsbUevent AddEvent(ushort vid = 0x03EB, ushort pid = 0x2FF4, ushort rev = 0x0936) =>
        new(IsAdd: true, DevPath: "/" + Path.GetFileName(NodeDir), vid, pid, rev);

    private string NodeDir { get; } = Directory.CreateTempSubdirectory("qmk-sysfs-test-").FullName;

    [Fact]
    public void BuildArrival_ReadsStringsFromSysfsAndIdentityFromUevent()
    {
        File.WriteAllText(Path.Combine(NodeDir, "manufacturer"), "QMK\n");
        File.WriteAllText(Path.Combine(NodeDir, "product"), "Keyboard\n");

        UsbDeviceInfo? device = LinuxUsbProbe.BuildArrival(AddEvent(), Path.GetDirectoryName(NodeDir)!);

        Assert.NotNull(device);
        Assert.Equal(0x03EB, device.VendorId);
        Assert.Equal(0x2FF4, device.ProductId);
        Assert.Equal(0x0936, device.RevisionBcd);
        Assert.Equal("QMK", device.ManufacturerString);
        Assert.Equal("Keyboard", device.ProductString);
        Assert.Equal(NodeDir, device.DevicePath);
    }

    [Fact]
    public void BuildArrival_Hub_Ignored()
    {
        File.WriteAllText(Path.Combine(NodeDir, "bDeviceClass"), "09\n");

        Assert.Null(LinuxUsbProbe.BuildArrival(AddEvent(vid: 0x1D6B, pid: 0x0002), Path.GetDirectoryName(NodeDir)!));
    }

    [Fact]
    public void BuildArrival_NoIdentity_Ignored() => Assert.Null(LinuxUsbProbe.BuildArrival(AddEvent(vid: 0, pid: 0, rev: 0), Path.GetDirectoryName(NodeDir)!));

    // ── startup sweep ─────────────────────────────────────────────────────────

    private string AddNode(string name, params (string Attribute, string Value)[] attributes)
    {
        string dir = Path.Combine(NodeDir, name);
        Directory.CreateDirectory(dir);
        foreach ((string attribute, string value) in attributes)
            File.WriteAllText(Path.Combine(dir, attribute), value);
        return dir;
    }

    [FactOnLinux]
    public void EnumeratePresent_DeviceNode_YieldsPopulatedDevice()
    {
        AddNode("1-2",
            ("idVendor", "03eb\n"), ("idProduct", "2ff4\n"), ("bcdDevice", "0936\n"),
            ("manufacturer", "QMK\n"), ("product", "Keyboard\n"));

        UsbDeviceInfo device = Assert.Single(LinuxUsbProbe.EnumeratePresent(NodeDir));

        Assert.Equal(0x03EB, device.VendorId);
        Assert.Equal(0x2FF4, device.ProductId);
        Assert.Equal(0x0936, device.RevisionBcd);
        Assert.Equal("QMK", device.ManufacturerString);
        Assert.Equal("Keyboard", device.ProductString);
        Assert.Equal(Path.Combine(NodeDir, "1-2"), device.DevicePath);
    }

    [FactOnLinux]
    public void EnumeratePresent_InterfaceNodesAndHubs_Skipped()
    {
        // An interface entry has no idVendor/idProduct; a hub reports bDeviceClass 09.
        AddNode("1-2:1.0", ("bInterfaceClass", "03\n"));
        AddNode("usb1", ("idVendor", "1d6b\n"), ("idProduct", "0002\n"), ("bDeviceClass", "09\n"));

        Assert.Empty(LinuxUsbProbe.EnumeratePresent(NodeDir));
    }

    [FactOnLinux]
    public void EnumeratePresent_UnparseableIds_Skipped()
    {
        AddNode("1-3", ("idVendor", "junk\n"), ("idProduct", "2ff4\n"));

        Assert.Empty(LinuxUsbProbe.EnumeratePresent(NodeDir));
    }

    [FactOnLinux]
    public void EnumeratePresent_MissingRoot_YieldsNothing() => Assert.Empty(LinuxUsbProbe.EnumeratePresent(Path.Combine(NodeDir, "does-not-exist")));
}
