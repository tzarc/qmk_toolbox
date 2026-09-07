namespace QmkToolbox.Usb.Discovery;

/// <summary>
/// What a probe reports for a removal: a device path, a VID/PID pair, or both. The device is
/// gone and cannot be queried, so the hint carries only what the platform's removal event supplies.
/// Leave VID/PID zero when the platform does not report them.
/// </summary>
/// <param name="DevicePath">The removed device's platform path, or empty when the platform drops it.</param>
/// <param name="VendorId">USB vendor ID, or zero when the platform does not report it on removal.</param>
/// <param name="ProductId">USB product ID, or zero when the platform does not report it on removal.</param>
public readonly record struct UsbRemovalHint(string DevicePath, ushort VendorId = 0, ushort ProductId = 0);
