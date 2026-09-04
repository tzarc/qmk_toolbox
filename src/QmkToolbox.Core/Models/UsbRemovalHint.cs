namespace QmkToolbox.Core.Models;

/// <summary>
/// The lossy payload of a USB removal event: platforms report a device path, a VID/PID pair, or
/// both, and nothing more, because the device is gone and cannot be queried. Carrying only
/// these fields makes "never query the OS on removal" structural rather than caller discipline.
/// A probe whose VID/PID is unknown leaves them zero.
/// </summary>
public readonly record struct UsbRemovalHint(string DevicePath, ushort VendorId = 0, ushort ProductId = 0);
