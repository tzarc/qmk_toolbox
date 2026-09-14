using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader;

/// <summary>Picks the volume a mass-storage bootloader is flashed through.</summary>
internal static class MarkerVolumes
{
    /// <summary>
    /// Returns the mount point of the volume <paramref name="device"/> backs that carries
    /// <paramref name="markerFile"/> at its root, or <see langword="null"/> when no such volume
    /// is mounted. When several volumes match, the first the resolver yields wins.
    /// </summary>
    public static string? FindMarkerVolume(this IUsbDeviceResolver resolver, UsbDeviceInfo device, string markerFile) =>
        // Windows drive roots enumerate as "E:\". Without the trim, the separator reaches the
        // device's display name and breaks mount-point comparison.
        resolver.EnumerateVolumes(device)
            .FirstOrDefault(mount => File.Exists(Path.Combine(mount, markerFile)))?
            .TrimEnd('\\', '/');
}
