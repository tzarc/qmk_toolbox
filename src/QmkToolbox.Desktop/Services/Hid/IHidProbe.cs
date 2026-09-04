namespace QmkToolbox.Desktop.Services.Hid;

/// <summary>
/// Identity of one HID collection. Two collections can share a device path on Linux
/// (multi-collection devices exposed through one hidraw node), so the usage pair is part of
/// the key.
/// </summary>
public readonly record struct HidDeviceKey(string DevicePath, ushort UsagePage, ushort Usage);

/// <summary>
/// Enumeration seam for <see cref="HidDeviceTracker"/>, mirroring the probe/tracker split in
/// Qmk.Usb.Discovery: the probe knows how to find and open console-capable devices; the
/// tracker owns polling, diffing, and device lifecycle. Members are called only from the
/// tracker's poll thread.
/// </summary>
public interface IHidProbe : IDisposable
{
    /// <summary>Prepares the underlying HID stack; called once before the first enumeration.</summary>
    void Start();

    /// <summary>Keys of every console-candidate device currently connected. Opens nothing.</summary>
    IReadOnlyList<HidDeviceKey> EnumerateKeys();

    /// <summary>
    /// Opens the device behind a key, or <see langword="null"/> when it vanished since
    /// enumeration or cannot be opened. The device raises
    /// <see cref="BaseHidDevice.ConsoleReportReceived"/> until disposed.
    /// </summary>
    BaseHidDevice? Open(HidDeviceKey key);
}
