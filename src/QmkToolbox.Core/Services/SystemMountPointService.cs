using Qmk.Usb.Discovery;

namespace QmkToolbox.Core.Services;

/// <summary>
/// Mount point service backed by <see cref="UsbVolumes"/>: of the volumes the device provably
/// backs, the first carrying the marker file wins.
/// </summary>
public sealed class SystemMountPointService : IMountPointService
{
    public string? FindMountPoint(UsbDeviceInfo device, string markerFile) =>
        device.EnumerateVolumes()
            .FirstOrDefault(mount => File.Exists(Path.Combine(mount, markerFile)))?
            .TrimEnd('\\', '/');
}
