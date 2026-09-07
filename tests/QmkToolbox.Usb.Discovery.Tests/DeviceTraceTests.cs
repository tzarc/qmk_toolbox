using Xunit;

namespace QmkToolbox.Usb.Discovery.Tests;

public class DeviceTraceTests
{
    [Fact]
    public void VidPid_FormatsUppercaseFourDigitHex()
        => Assert.Equal("VID:03EB PID:2FF4", DeviceTrace.VidPid(new UsbDeviceInfo(0x03EB, 0x2FF4, 0, "", "", "", "")));

    [Fact]
    public void VidPidRev_IncludesRevision()
        => Assert.Equal("VID:03EB PID:2FF4 REV:0936",
            DeviceTrace.VidPidRev(new UsbDeviceInfo(0x03EB, 0x2FF4, 0x0936, "", "", "", "")));

    [Theory]
    [InlineData(null, "(empty)")]
    [InlineData("", "(empty)")]
    [InlineData("/dev/bus/usb/001/002", "\"/dev/bus/usb/001/002\"")]
    public void Path_QuotesOrMarksEmpty(string? path, string expected)
        => Assert.Equal(expected, DeviceTrace.Path(path));
}
