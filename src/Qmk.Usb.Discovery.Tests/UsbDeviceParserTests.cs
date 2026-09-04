using Xunit;

namespace Qmk.Usb.Discovery.Tests;

public class UsbDeviceParserTests
{
    // ── TryParseUsbId ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0x0483", 0x0483)] // Linux: "0x"-prefixed hex, lower case prefix
    [InlineData("0X0483", 0x0483)] // Linux: "0X"-prefixed hex, upper case prefix
    [InlineData("0xFFFF", 0xFFFF)] // max value with prefix
    [InlineData("0483", 0x0483)]   // Windows/Linux: bare 4-digit hex
    [InlineData("DF11", 0xDF11)]   // Windows/Linux: bare hex with letters
    [InlineData("FFFF", 0xFFFF)]   // Windows/Linux: max bare hex
    public void TryParseUsbId_ParsesHex(string input, ushort expected)
    {
        bool ok = UsbDeviceParser.TryParseUsbId(input, out ushort value);
        Assert.True(ok);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryParseUsbId_NullOrEmpty_ReturnsFalse(string? input) =>
        Assert.False(UsbDeviceParser.TryParseUsbId(input, out _));

    [Theory]
    [InlineData("ZZZZ")]   // not valid hex
    [InlineData("0xGGGG")] // invalid after 0x prefix
    [InlineData("10000")]  // overflows ushort (hex): 65536
    public void TryParseUsbId_Invalid_ReturnsFalse(string input) =>
        Assert.False(UsbDeviceParser.TryParseUsbId(input, out _));

    // ── TryParseHwId ──────────────────────────────────────────────────────────

    [Fact]
    public void TryParseHwId_BasicVidPid_ParsesCorrectly()
    {
        bool ok = UsbDeviceParser.TryParseHwId(
            @"USB\VID_0483&PID_DF11\5&2D4F03CB&0&2",
            out ushort vid, out ushort pid, out ushort rev);

        Assert.True(ok);
        Assert.Equal(0x0483, vid);
        Assert.Equal(0xDF11, pid);
        Assert.Equal(0x0000, rev);
    }

    [Fact]
    public void TryParseHwId_WithRevision_ParsesRev()
    {
        bool ok = UsbDeviceParser.TryParseHwId(
            @"USB\VID_03EB&PID_2FFB&REV_0200\5&0",
            out ushort vid, out ushort pid, out ushort rev);

        Assert.True(ok);
        Assert.Equal(0x03EB, vid);
        Assert.Equal(0x2FFB, pid);
        Assert.Equal(0x0200, rev);
    }

    [Fact]
    public void TryParseHwId_LowercasePath_ParsesCorrectly()
    {
        bool ok = UsbDeviceParser.TryParseHwId(
            @"usb\vid_0483&pid_df11\5",
            out ushort vid, out ushort pid, out ushort rev);

        Assert.True(ok);
        Assert.Equal(0x0483, vid);
        Assert.Equal(0xDF11, pid);
        Assert.Equal(0x0000, rev);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a usb path")]
    [InlineData(@"ACPI\ACPI0005\2&1ABC&0")] // no VID_/PID_ pattern at all
    public void TryParseHwId_NoMatch_ReturnsFalse(string input) =>
        Assert.False(UsbDeviceParser.TryParseHwId(input, out _, out _, out _));

    // ── TryParseRevisionFromHardwareIds ────────────────────────────────────────

    [Fact]
    public void TryParseRevisionFromHardwareIds_RealisticMultiSz_FindsRev()
    {
        // CM_DRP_HARDWAREID as Windows reports it: most-specific entry first.
        string[] hardwareIds =
        [
            @"USB\VID_03EB&PID_2FF4&REV_0936",
            @"USB\VID_03EB&PID_2FF4",
        ];

        Assert.True(UsbDeviceParser.TryParseRevisionFromHardwareIds(hardwareIds, out ushort rev));
        Assert.Equal(0x0936, rev);
    }

    [Fact]
    public void TryParseRevisionFromHardwareIds_NoRevEntry_ReturnsFalse()
    {
        Assert.False(UsbDeviceParser.TryParseRevisionFromHardwareIds(
            [@"USB\VID_03EB&PID_2FF4"], out _));
    }

    [Fact]
    public void TryParseRevisionFromHardwareIds_EmptyList_ReturnsFalse()
        => Assert.False(UsbDeviceParser.TryParseRevisionFromHardwareIds([], out _));

    // ── TryParseBcdDevice ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("0936\n", 0x0936)] // sysfs value with trailing newline
    [InlineData("0200", 0x0200)]
    [InlineData("FFFF", 0xFFFF)]
    public void TryParseBcdDevice_SysfsValue_ParsesHex(string input, int expected)
    {
        Assert.True(UsbDeviceParser.TryParseBcdDevice(input, out ushort rev));
        Assert.Equal((ushort)expected, rev);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not hex")]
    public void TryParseBcdDevice_Invalid_ReturnsFalse(string? input)
        => Assert.False(UsbDeviceParser.TryParseBcdDevice(input, out _));
}
