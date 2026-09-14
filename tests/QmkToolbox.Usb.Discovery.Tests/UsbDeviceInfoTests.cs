using Xunit;

namespace QmkToolbox.Usb.Discovery.Tests;

public class UsbDeviceInfoTests
{
    private static UsbDeviceInfo Device(ushort rev = 0, string devicePath = "") =>
        new(0x03EB, 0x2FF4, rev, "", "", devicePath);

    private static UsbDeviceInfo Composite() =>
        new(0x1209, 0xDB42, 0, "", "", "", drivers: ["WinUSB", "USBSTOR"]);

    [Fact]
    public void TraceVidPid_FormatsUppercaseFourDigitHex()
        => Assert.Equal("VID:03EB PID:2FF4", Device().TraceVidPid);

    [Fact]
    public void TraceVidPidRev_IncludesRevision()
        => Assert.Equal("VID:03EB PID:2FF4 REV:0936", Device(rev: 0x0936).TraceVidPidRev);

    [Theory]
    [InlineData("", "(empty)")]
    [InlineData("/dev/bus/usb/001/002", "\"/dev/bus/usb/001/002\"")]
    public void TracePath_QuotesOrMarksEmpty(string devicePath, string expected)
        => Assert.Equal(expected, Device(devicePath: devicePath).TracePath);

    [Fact]
    public void Drivers_AreEmptyWhenNoDriverIsReported()
        => Assert.Empty(Device().Drivers);

    [Fact]
    public void Drivers_KeepEveryFunctionOfACompositeDevice()
    {
        IReadOnlySet<string> drivers = Composite().Drivers;
        Assert.Equal(2, drivers.Count);
        Assert.Contains("WinUSB", drivers);
        Assert.Contains("USBSTOR", drivers);
    }

    [Fact]
    public void Drivers_MatchRegardlessOfCase()
        => Assert.Contains("usbstor", Composite().Drivers);
}
