using Qmk.Usb.Discovery;

namespace QmkToolbox.Core.Services;

/// <summary>
/// Resolves the filesystem mount point associated with a USB mass-storage device.
/// </summary>
public interface IMountPointService
{
    /// <summary>
    /// Returns the mount point path for <paramref name="device"/>, or <see langword="null"/>
    /// if the device is not mounted. Only volumes the device provably backs qualify, and of
    /// those only one carrying <paramref name="markerFile"/> at its root is returned.
    /// </summary>
    string? FindMountPoint(UsbDeviceInfo device, string markerFile);
}
