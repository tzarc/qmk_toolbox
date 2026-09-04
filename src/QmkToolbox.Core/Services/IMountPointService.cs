using Qmk.Usb.Discovery;

namespace QmkToolbox.Core.Services;

/// <summary>
/// Resolves the filesystem mount point associated with a USB mass-storage device.
/// </summary>
public interface IMountPointService
{
    /// <summary>
    /// Returns the mount point path for <paramref name="device"/>, or <see langword="null"/>
    /// if the device is not mounted. Only volumes carrying <paramref name="markerFile"/> at
    /// their root qualify, and a volume provably backed by a different USB device is never
    /// returned; a volume whose ownership cannot be determined is accepted.
    /// </summary>
    string? FindMountPoint(UsbDeviceInfo device, string markerFile);
}
