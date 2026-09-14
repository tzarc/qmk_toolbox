using QmkToolbox.Core.Models;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Recognises mass-storage devices that carry no known VID/PID by waiting for one to mount a
/// volume holding a bootloader family's marker file. Marker-probed bootloaders use per-board
/// VID/PIDs, so the marker is the only general way to identify them, and the volume appears only
/// once the OS (or the user, on desktops that don't automount) mounts the drive. The probe
/// therefore runs as long as the device stays connected; <see cref="Cancel"/> ends it when the
/// device is unplugged.
/// </summary>
internal sealed class MarkerVolumeProbe(BootloaderServices services)
{
    // One entry per device in flight, so a disconnect cancels only that device's probe.
    private readonly List<(UsbDeviceInfo Device, CancellationTokenSource Cancellation)> _probes = [];
    private readonly Lock _probesLock = new();

    /// <summary>
    /// Returns the bootloader device once <paramref name="device"/> mounts a volume carrying a
    /// probeable family's marker file, or <see langword="null"/> when it is not mass storage or
    /// the probe is cancelled. Completion can lag the arrival by however long the user takes to
    /// mount the drive.
    /// </summary>
    /// <param name="isMountClaimed">Reports mounts already backing a registered device. A marker
    /// volume can only be claimed once, so with several unknown devices probing at once (a thumb
    /// drive alongside a keyboard) the second must keep waiting rather than claim the first's.</param>
    public async Task<BootloaderDevice?> FindBootloaderAsync(UsbDeviceInfo device, Func<string, bool> isMountClaimed)
    {
        if (!device.IsMassStorage)
            return null;

        // Every stage of the arrival pipeline shares the [ORCH+] prefix in the Debug Log.
        services.Output($"[ORCH+] {device.TraceVidPidRev} -> mass storage, probing for" +
            $" {string.Join(", ", MassStorageBootloader.Probeable.Select(f => f.MarkerFile))} until removal",
            MessageType.Debug);

        var cancellation = new CancellationTokenSource();
        lock (_probesLock)
            _probes.Add((device, cancellation));
        try
        {
            while (true)
            {
                foreach (MassStorageBootloader family in MassStorageBootloader.Probeable)
                {
                    string? mount = services.UsbResolver.FindMarkerVolume(device, family.MarkerFile);
                    if (mount == null || isMountClaimed(mount))
                        continue;
                    string? boardId = family.BoardIdReader?.Invoke(Path.Combine(mount, family.MarkerFile));
                    services.Output(
                        $"[ORCH+] {device.TraceVidPidRev} -> {family.Name} volume at \"{mount}\"" +
                        (boardId == null ? "" : $" (Board-ID: {boardId})"), MessageType.Debug);
                    return BootloaderFactory.CreateMassStorageDevice(family.Type, device, services, boardId, mount);
                }
                await Task.Delay(services.VolumeProbeDelayMs, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            services.Output(
                $"[ORCH+] {device.TraceVidPidRev} -> volume probe ended, device removed", MessageType.Debug);
            return null;
        }
        finally
        {
            lock (_probesLock)
                _probes.RemoveAll(p => p.Cancellation == cancellation);
            cancellation.Dispose();
        }
    }

    /// <summary>Ends <paramref name="device"/>'s probe, if one is running.</summary>
    // The detector guarantees a removal delivers the identical UsbDeviceInfo instance it
    // announced at arrival (see IUsbEventsDetector.DeviceDisconnected), so matching uses
    // reference identity rather than path or VID/PID.
    public void Cancel(UsbDeviceInfo device)
    {
        CancellationTokenSource? cancellation;
        lock (_probesLock)
            cancellation = _probes.FirstOrDefault(p => p.Device == device).Cancellation;
        cancellation?.Cancel();
    }
}
