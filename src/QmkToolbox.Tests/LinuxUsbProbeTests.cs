using QmkToolbox.Core.Models;
using QmkToolbox.Desktop.Services;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Drives the Linux probe's arrival-payload conversion (realistic Usb.Events payloads against a
/// fixture-owned fake sysfs node; the coverage that would have caught F1, RevisionBcd never
/// populated) and the /sys/bus/usb startup sweep over a fixture-owned fake sysfs tree.
/// Linux-only: enrichment goes through the real sysfs read path.
/// </summary>
public sealed class LinuxUsbProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("qmk-sysfs-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ── arrival conversion ────────────────────────────────────────────────────

    // The values a Usb.Events arrival carries on Linux: udev hex IDs and the device syspath.
    private UsbDeviceInfo? Convert() => new LinuxUsbProbe().ToDeviceInfo(
        vendorId: "0x03eb", productId: "0x2ff4", vendor: "QMK", product: "Atmel DFU",
        deviceSystemPath: _root);

    [FactOnLinux]
    public void Arrival_QmkDfuPayload_YieldsRevisionFromSysfs()
    {
        File.WriteAllText(Path.Combine(_root, "bcdDevice"), "0936\n");

        UsbDeviceInfo? device = Convert();

        Assert.NotNull(device);
        Assert.Equal(0x03EB, device.VendorId);
        Assert.Equal(0x2FF4, device.ProductId);
        Assert.Equal(0x0936, device.RevisionBcd);
    }

    [FactOnLinux]
    public void Arrival_NoBcdDeviceAttribute_YieldsZeroRevision()
    {
        UsbDeviceInfo? device = Convert();

        Assert.NotNull(device);
        Assert.Equal(0, device.RevisionBcd);
    }

    [FactOnLinux]
    public void Arrival_UnreadableBcdDeviceAttribute_YieldsZeroRevision()
    {
        File.WriteAllText(Path.Combine(_root, "bcdDevice"), "not hex at all");

        UsbDeviceInfo? device = Convert();

        Assert.NotNull(device);
        Assert.Equal(0, device.RevisionBcd);
    }

    // ── startup sweep ─────────────────────────────────────────────────────────

    private string AddNode(string name, params (string Attribute, string Value)[] attributes)
    {
        string dir = Path.Combine(_root, name);
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

        UsbDeviceInfo device = Assert.Single(LinuxUsbProbe.EnumeratePresent(_root));

        Assert.Equal(0x03EB, device.VendorId);
        Assert.Equal(0x2FF4, device.ProductId);
        Assert.Equal(0x0936, device.RevisionBcd);
        Assert.Equal("QMK", device.ManufacturerString);
        Assert.Equal("Keyboard", device.ProductString);
        Assert.Equal(Path.Combine(_root, "1-2"), device.DevicePath);
    }

    [FactOnLinux]
    public void EnumeratePresent_InterfaceNodesAndHubs_Skipped()
    {
        // An interface entry has no idVendor/idProduct; a hub reports bDeviceClass 09.
        AddNode("1-2:1.0", ("bInterfaceClass", "03\n"));
        AddNode("usb1", ("idVendor", "1d6b\n"), ("idProduct", "0002\n"), ("bDeviceClass", "09\n"));

        Assert.Empty(LinuxUsbProbe.EnumeratePresent(_root));
    }

    [FactOnLinux]
    public void EnumeratePresent_UnparseableIds_Skipped()
    {
        AddNode("1-3", ("idVendor", "junk\n"), ("idProduct", "2ff4\n"));

        Assert.Empty(LinuxUsbProbe.EnumeratePresent(_root));
    }

    [FactOnLinux]
    public void EnumeratePresent_MissingRoot_YieldsNothing() => Assert.Empty(LinuxUsbProbe.EnumeratePresent(Path.Combine(_root, "does-not-exist")));
}
