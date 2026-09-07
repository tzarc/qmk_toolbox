using Qmk.Usb.Discovery;
using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Bootloader.Impl;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

/// <summary>
/// Tracks connected bootloader devices and runs flash / reset / EEPROM operations against
/// them. Thread-safe: device events and commands may arrive on any thread, and
/// <see cref="OutputReceived"/> and <see cref="StateChanged"/> are raised on whichever
/// thread triggered them, so subscribers marshal to the UI themselves.
/// </summary>
public class FlashOrchestrator(BootloaderServices services) : IDisposable
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    // Guards _bootloaders and _volumeProbes.
    private readonly Lock _stateLock = new();

    // Admits the single in-flight operation of RunExclusiveAsync; a second attempt is
    // refused rather than queued.
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private readonly List<BootloaderDevice> _bootloaders = [];

    // One pending volume probe per unknown mass-storage device (mostly thumb drives, not
    // bootloaders); tracked so a disconnect can cancel that device's probe.
    private readonly List<(UsbDeviceInfo Device, CancellationTokenSource Cancellation)> _volumeProbes = [];

    // Every unknown mass-storage device is polled for a marker volume for as long as it
    // stays connected; desktops like KDE mount removable drives only when the user asks,
    // which can be minutes after the USB arrival. Init-only so tests can shrink the cadence.
    public int VolumeProbeDelayMs { get; init; } = 250;

    public event Action<string, MessageType>? OutputReceived;
    public event Action? StateChanged;

    public Action<string>? DiagnosticTrace { get; set; }

    public bool HasBootloaders => BootloaderCount > 0;
    public bool HasResettable => SnapshotBootloaders().Any(b => b.IsResettable);
    public bool HasEepromFlashable => SnapshotBootloaders().Any(b => b.IsEepromFlashable);

    public int BootloaderCount
    {
        get { lock (_stateLock) return _bootloaders.Count; }
    }

    /// <summary> True while a flash / reset / EEPROM / resource-maintenance operation is running. </summary>
    public bool IsBusy => _operationGate.CurrentCount == 0;

    private List<BootloaderDevice> SnapshotBootloaders()
    {
        lock (_stateLock)
            return [.. _bootloaders];
    }

    /// <summary>
    /// Registers a connected USB device as a bootloader if recognised.
    /// Returns <see langword="true"/> if a bootloader device was added (caller may trigger auto-flash).
    /// Devices outside the VID/PID map that expose a mass-storage interface are probed for a
    /// volume carrying a bootloader marker file until it mounts or the device is removed, so
    /// completion can lag the arrival by however long the user takes to mount the drive.
    /// </summary>
    public async Task<bool> OnDeviceConnectedAsync(UsbDeviceInfo device, bool showAllDevices)
    {
        BootloaderDevice? bd = BootloaderFactory.CreateDevice(device, services);
        if (bd == null)
        {
            // Report unknown devices right away: the volume probe below can run for the
            // device's whole lifetime, and nothing user-visible may wait on it.
            if (showAllDevices)
                Emit($"USB device connected{WindowsDriverSuffix(device.Driver)}: {device}", MessageType.Usb);
            DiagnosticTrace?.Invoke(
                $"[ORCH+] {DeviceTrace.VidPidRev(device)} -> not a bootloader");
            bd = await TryCreateMassStorageDeviceAsync(device).ConfigureAwait(false);
            if (bd == null)
                return false;
        }

        bd.OutputReceived += OnFlashOutput;
        lock (_stateLock)
            _bootloaders.Add(bd);
        DiagnosticTrace?.Invoke(
            $"[ORCH+] {DeviceTrace.VidPidRev(device)} path:{DeviceTrace.Path(device.DevicePath)}" +
            $" -> {bd.Name}  (bootloaders:{BootloaderCount})");
        StateChanged?.Invoke();
        // Await port resolution (instant for most devices; up to ~2.5 s for serial-port
        // bootloaders) so the connected message includes the resolved port in ToString().
        _ = bd.WhenReadyAsync().ContinueWith(_ =>
        {
            Emit($"{bd.Name} device connected{WindowsDriverSuffix(bd.Driver)}: {bd}", MessageType.Bootloader);
            if (IsWindows && !string.IsNullOrEmpty(bd.Driver) && !string.IsNullOrEmpty(bd.PreferredDriver) && bd.PreferredDriver != bd.Driver)
                Emit($"{bd.Name} device has {bd.Driver} driver assigned but should be {bd.PreferredDriver}. Flashing may not succeed.", MessageType.Error);
        }, TaskScheduler.Default);
        return true;
    }

    /// <summary>
    /// Polls a mass-storage device outside the VID/PID map for a volume carrying one of the
    /// probeable families' marker files (<see cref="MassStorageBootloader.Probeable"/>) until
    /// one appears or the device is removed. Marker-probed bootloaders carry per-board
    /// VID/PIDs, so the marker is the only general way to recognise them, and the volume
    /// appears only when the OS (or the user, on desktops that don't automount) mounts the
    /// drive.
    /// </summary>
    private async Task<BootloaderDevice?> TryCreateMassStorageDeviceAsync(UsbDeviceInfo device)
    {
        if (!device.IsMassStorage || services.MountPoints is not { } mountPoints)
            return null;
        DiagnosticTrace?.Invoke(
            $"[ORCH+] {DeviceTrace.VidPidRev(device)} -> mass storage, probing for" +
            $" {string.Join(", ", MassStorageBootloader.Probeable.Select(f => f.MarkerFile))} until removal");
        var cancellation = new CancellationTokenSource();
        lock (_stateLock)
            _volumeProbes.Add((device, cancellation));
        try
        {
            while (true)
            {
                foreach (MassStorageBootloader family in MassStorageBootloader.Probeable)
                {
                    string? mount = mountPoints.FindMountPoint(device, family.MarkerFile);
                    if (mount == null || IsMountClaimed(mount))
                        continue;
                    string? boardId = family.BoardIdReader?.Invoke(Path.Combine(mount, family.MarkerFile));
                    DiagnosticTrace?.Invoke(
                        $"[ORCH+] {DeviceTrace.VidPidRev(device)} -> {family.Name} volume at \"{mount}\"" +
                        (boardId == null ? "" : $" (Board-ID: {boardId})"));
                    return BootloaderFactory.CreateMassStorageDevice(family.Type, device, services, boardId, mount);
                }
                await Task.Delay(VolumeProbeDelayMs, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            DiagnosticTrace?.Invoke(
                $"[ORCH+] {DeviceTrace.VidPidRev(device)} -> volume probe ended, device removed");
            return null;
        }
        finally
        {
            lock (_stateLock)
                _volumeProbes.RemoveAll(p => p.Cancellation == cancellation);
            cancellation.Dispose();
        }
    }

    // A marker volume already backing a registered mass-storage device can't be claimed
    // again; with several unknown devices probing at once (e.g. a thumb drive alongside a
    // keyboard), only one may register per volume.
    private bool IsMountClaimed(string mount) =>
        SnapshotBootloaders().Any(b => b is MassStorageDevice ms && ms.MountPoint == mount);

    // The detector guarantees a removal delivers the identical UsbDeviceInfo instance it announced
    // at arrival (see IUsbEventsDetector.DeviceDisconnected), so matching uses reference identity
    // rather than path or VID/PID.
    private void CancelVolumeProbe(UsbDeviceInfo device)
    {
        CancellationTokenSource? cancellation;
        lock (_stateLock)
            cancellation = _volumeProbes.FirstOrDefault(p => p.Device == device).Cancellation;
        cancellation?.Cancel();
    }

    public void OnDeviceDisconnected(UsbDeviceInfo device, bool showAllDevices)
    {
        CancelVolumeProbe(device);

        BootloaderDevice? bd;
        int remaining;
        lock (_stateLock)
        {
            bd = _bootloaders.FirstOrDefault(b => b.Device == device);
            if (bd != null)
                _bootloaders.Remove(bd);
            remaining = _bootloaders.Count;
        }

        if (bd != null)
        {
            bd.OutputReceived -= OnFlashOutput;
            Emit($"{bd.Name} device disconnected{WindowsDriverSuffix(bd.Driver)}: {bd}", MessageType.Bootloader);
        }
        else if (showAllDevices)
        {
            Emit($"USB device disconnected{WindowsDriverSuffix(device.Driver)}: {device}", MessageType.Usb);
        }

        if (DiagnosticTrace != null)
        {
            string prefix = $"[ORCH-] {DeviceTrace.VidPid(device)} path:{DeviceTrace.Path(device.DevicePath)}";
            if (bd != null)
            {
                DiagnosticTrace($"{prefix} -> matched  (bootloaders:{remaining})");
            }
            else if (remaining > 0)
            {
                DiagnosticTrace(
                    $"{prefix} -> *** no match  (bootloaders:{remaining} - possible phantom entry)");
            }
            else
            {
                DiagnosticTrace($"{prefix} -> not a tracked bootloader  (bootloaders:0)");
            }
        }

        StateChanged?.Invoke();
    }

    /// <summary>
    /// Runs <paramref name="operation"/> as the single in-flight flash / reset / EEPROM /
    /// resource-maintenance operation. Returns <see langword="true"/> if it ran, or
    /// <see langword="false"/> without running it when another operation is in progress.
    /// </summary>
    public async Task<bool> RunExclusiveAsync(Func<Task> operation)
    {
        if (!await _operationGate.WaitAsync(0).ConfigureAwait(false))
            return false;

        StateChanged?.Invoke();
        try
        {
            await operation().ConfigureAwait(false);
            return true;
        }
        finally
        {
            _operationGate.Release();
            StateChanged?.Invoke();
        }
    }

    public Task<bool> FlashAllAsync(string mcu, string firmwarePath) =>
        RunExclusiveAsync(() => FlashAllCoreAsync(mcu, firmwarePath));

    public Task<bool> ResetAllAsync(string mcu) =>
        RunExclusiveAsync(() => ResetAllCoreAsync(mcu));

    public Task<bool> FlashEepromAsync(string mcu, string fileName, string startMessage, string completeMessage) =>
        RunExclusiveAsync(() => FlashEepromCoreAsync(mcu, fileName, startMessage, completeMessage));

    private async Task FlashAllCoreAsync(string mcu, string firmwarePath)
    {
        DiagnosticTrace?.Invoke($"[FLASH] FlashAllAsync start  (bootloaders:{BootloaderCount})");
        try
        {
            foreach (BootloaderDevice b in SnapshotBootloaders())
            {
                try
                {
                    Emit("Attempting to flash, please don't remove device", MessageType.Bootloader);
                    await b.FlashAsync(mcu, firmwarePath).ConfigureAwait(false);
                    Emit("Flash complete", MessageType.Bootloader);
                }
                catch (Exception ex) when (ex is UnsupportedFileFormatException or ComPortNotFoundException)
                {
                    Emit(ex.Message, MessageType.Error);
                }
            }
        }
        finally
        {
            DiagnosticTrace?.Invoke($"[FLASH] FlashAllAsync finally  (bootloaders:{BootloaderCount})");
        }
    }

    private async Task ResetAllCoreAsync(string mcu)
    {
        DiagnosticTrace?.Invoke($"[RESET] ResetAllAsync start  (bootloaders:{BootloaderCount})");
        foreach (BootloaderDevice b in SnapshotBootloaders().Where(b => b.IsResettable))
        {
            try
            {
                await b.ResetAsync(mcu).ConfigureAwait(false);
            }
            catch (ComPortNotFoundException ex)
            {
                Emit(ex.Message, MessageType.Error);
            }
        }
    }

    private async Task FlashEepromCoreAsync(string mcu, string fileName, string startMessage, string completeMessage)
    {
        foreach (BootloaderDevice b in SnapshotBootloaders().Where(b => b.IsEepromFlashable))
        {
            try
            {
                Emit(startMessage, MessageType.Bootloader);
                await b.FlashEepromAsync(mcu, fileName).ConfigureAwait(false);
                Emit(completeMessage, MessageType.Bootloader);
            }
            catch (ComPortNotFoundException ex)
            {
                Emit(ex.Message, MessageType.Error);
            }
        }
    }

    private void OnFlashOutput(BootloaderDevice device, string data, MessageType type) => Emit(data, type);

    private void Emit(string message, MessageType type) => OutputReceived?.Invoke(message, type);

    // Driver info is Windows-only; on other platforms the field is always empty.
    // Matches upstream behaviour: show the driver name, or NO DRIVER if none is assigned.
    private static string WindowsDriverSuffix(string driver) =>
        IsWindows ? $" ({(string.IsNullOrEmpty(driver) ? "NO DRIVER" : driver)})" : "";

    public void Dispose()
    {
        _operationGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
