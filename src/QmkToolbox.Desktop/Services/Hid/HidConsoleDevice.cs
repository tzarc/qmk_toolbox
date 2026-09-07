using System.Text;
using QmkToolbox.Usb.Hid;

namespace QmkToolbox.Desktop.Services.Hid;

/// <summary>
/// A QMK HID console device (usage page 0xFF31, usage 0x0074): decodes the null-padded
/// UTF-8 report stream into <see cref="BaseHidDevice.ConsoleReportReceived"/> text events.
/// The console is a raw byte stream chunked into reports; the consumer's terminal buffer
/// interprets '\r'/'\n'.
/// </summary>
public sealed class HidConsoleDevice : BaseHidDevice, IDisposable
{
    public const ushort TargetUsagePage = 0xFF31;
    public const ushort TargetUsage = 0x0074;

    /// <inheritdoc />
    public override bool IsConsoleDevice => true;

    public static bool Match(HidInterfaceInfo iface) =>
        iface.UsagePage == TargetUsagePage && iface.Usage == TargetUsage;

    public static BaseHidDevice? TryCreate(HidInterfaceInfo iface) =>
        Match(iface) && iface.Open() is { } channel ? new HidConsoleDevice(iface, channel) : null;

    private readonly HidInterfaceDevice _channel;
    // The stateful decoder handles multi-byte characters that span a report boundary.
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly char[] _charBuffer = new char[Encoding.UTF8.GetMaxCharCount(1024)];

    private HidConsoleDevice(HidInterfaceInfo iface, HidInterfaceDevice channel) : base(iface)
    {
        _channel = channel;
        _channel.ReportReceived += OnReport;
        _channel.Start();
    }

    private void OnReport(byte[] report)
    {
        // HID reports are null-padded; truncate at the first null byte.
        int validBytes = Array.IndexOf(report, (byte)0) is >= 0 and var nul ? nul : report.Length;
        if (validBytes == 0)
            return;
        int charCount = _decoder.GetChars(report, 0, Math.Min(validBytes, 1024), _charBuffer, 0);
        if (charCount > 0)
            RaiseConsoleReport(new string(_charBuffer, 0, charCount));
    }

    public void Dispose()
    {
        _channel.ReportReceived -= OnReport;
        _channel.Dispose();
    }
}
