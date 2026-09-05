using Qmk.Usb.Discovery;
using QmkToolbox.Desktop.Services;
using Xunit;

namespace QmkToolbox.Tests;

/// <summary>
/// Exercises the /dev/serial/by-id lookup against a temp directory shaped like udev's output:
/// symlinks named "usb-VENDOR_PRODUCT_VID_PID_SERIAL-ifNN" pointing at device nodes.
/// </summary>
public sealed class DesktopSerialPortServiceTests : IDisposable
{
    private readonly string _byIdDir = Directory.CreateTempSubdirectory("by-id-test-").FullName;

    private static UsbDeviceInfo Device(ushort vid, ushort pid) =>
        new(vid, pid, 0x0100, "", "", "", "");

    public void Dispose() => Directory.Delete(_byIdDir, recursive: true);

    private string AddLink(string linkName, string targetName)
    {
        string target = Path.Combine(_byIdDir, targetName);
        File.WriteAllText(target, "");
        File.CreateSymbolicLink(Path.Combine(_byIdDir, linkName), target);
        return target;
    }

    [FactOnLinux]
    public void MatchingVidPid_ReturnsTheResolvedDeviceNode()
    {
        string target = AddLink("usb-QMK_Planck_FEED_6060_1234-if00", "ttyACM0");

        Assert.Equal(target, DesktopSerialPortService.FindByIdLinux(Device(0xFEED, 0x6060), _byIdDir));
    }

    [FactOnLinux]
    public void OtherDevicesVidPid_DoesNotMatch()
    {
        AddLink("usb-QMK_Planck_FEED_6060_1234-if00", "ttyACM0");

        Assert.Null(DesktopSerialPortService.FindByIdLinux(Device(0x2341, 0x0043), _byIdDir));
    }

    // VID digits inside another device's serial number must not match; only the combined
    // "VID_PID" token does.
    [FactOnLinux]
    public void VidInsideAnotherSerialNumber_DoesNotMatch()
    {
        AddLink("usb-Arduino_Uno_2341_0043_FEED-if00", "ttyACM1");

        Assert.Null(DesktopSerialPortService.FindByIdLinux(Device(0xFEED, 0x6060), _byIdDir));
    }

    [Fact]
    public void MissingDirectory_ReturnsNull() =>
        Assert.Null(DesktopSerialPortService.FindByIdLinux(
            Device(0xFEED, 0x6060), Path.Combine(_byIdDir, "does-not-exist")));
}
