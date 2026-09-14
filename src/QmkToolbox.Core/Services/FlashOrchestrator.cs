using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Bootloader.Impl;
using QmkToolbox.Core.Models;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Services;

/// <summary>
/// Tracks connected bootloader devices and runs flash / reset / EEPROM operations against
/// them. Thread-safe: device events and commands may arrive on any thread, and both the sink
/// and <see cref="StateChanged"/> fire on whichever thread triggered them, so the sink and any
/// subscriber marshal to the UI themselves.
/// </summary>
public class FlashOrchestrator(BootloaderServices services) : IDisposable
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    // Guards _bootloaders.
    private readonly Lock _stateLock = new();

    // Allows a single in-flight operation for RunExclusiveAsync; a second attempt is refused
    // rather than queued.
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private readonly List<BootloaderDevice> _bootloaders = [];

    // The device the in-flight operation is working on, and the source that abandons it. Both
    // are guarded by _stateLock, which orders them against a removal arriving on another thread.
    private BootloaderDevice? _operationDevice;
    private CancellationTokenSource? _operationCts;

    private readonly MarkerVolumeProbe _markerVolumeProbe = new(services);

    public event Action? StateChanged;

    public bool HasBootloaders => BootloaderCount > 0;
    public bool HasResettable => SnapshotBootloaders().Any(b => b.IsResettable);
    public bool HasEepromFlashable => SnapshotBootloaders().Any(b => b.IsEepromFlashable);

    public int BootloaderCount
    {
        get { lock (_stateLock) return _bootloaders.Count; }
    }

    /// <summary>True while a flash / reset / EEPROM / resource-maintenance operation is running.</summary>
    public bool IsBusy => _operationGate.CurrentCount == 0;

    private List<BootloaderDevice> SnapshotBootloaders()
    {
        lock (_stateLock)
            return [.. _bootloaders];
    }

    /// <summary>
    /// Registers a connected USB device as a bootloader if recognised.
    /// Returns <see langword="true"/> if a bootloader device was added (caller may trigger auto-flash).
    /// Devices outside the VID/PID map go to <see cref="MarkerVolumeProbe"/>, so completion
    /// can lag the arrival by however long the user takes to mount a drive.
    /// </summary>
    public async Task<bool> OnDeviceConnectedAsync(UsbDeviceInfo device, bool showAllDevices)
    {
        BootloaderDevice? bd = BootloaderFactory.CreateDevice(device, services);
        if (bd == null)
        {
            // Report unknown devices right away: the volume probe below can run for the
            // device's whole lifetime, and nothing user-visible may wait on it.
            if (showAllDevices)
                Output($"USB device connected{WindowsDriverSuffix(device)}: {device}", MessageType.Usb);
            Output($"[ORCH+] {device.TraceVidPidRev} -> not a bootloader", MessageType.Debug);
            bd = await _markerVolumeProbe.FindBootloaderAsync(device, IsMountClaimed).ConfigureAwait(false);
            if (bd == null)
                return false;
        }

        lock (_stateLock)
            _bootloaders.Add(bd);
        Output(
            $"[ORCH+] {device.TraceVidPidRev} path:{device.TracePath}" +
            $" -> {bd.Name}  (bootloaders:{BootloaderCount})", MessageType.Debug);
        StateChanged?.Invoke();
        // Await port resolution (instant for most devices; up to ~2.5 s for serial-port
        // bootloaders) so the connected message includes the resolved port in ToString().
        _ = bd.WhenReadyAsync().ContinueWith(_ =>
        {
            Output($"{bd.Name} device connected{WindowsDriverSuffix(bd)}: {bd}", MessageType.Bootloader);
            // A composite device binds one driver per function, so any of them satisfies the
            // preference. A match still cannot prove the bootloader's own interface holds it;
            // see UsbDeviceInfo.Drivers before narrowing this.
            if (IsWindows && bd.Drivers.Count > 0 && !string.IsNullOrEmpty(bd.PreferredDriver)
                && !bd.Drivers.Contains(bd.PreferredDriver))
            {
                Output($"{bd.Name} device has {Join(bd.Drivers)} assigned but should be {bd.PreferredDriver}. Flashing may not succeed.", MessageType.Error);
            }
        }, TaskScheduler.Default);
        return true;
    }

    // A marker volume already backing a registered mass-storage device can't be claimed again.
    private bool IsMountClaimed(string mount) =>
        SnapshotBootloaders().Any(b => b is MassStorageDevice ms && ms.MountPoint == mount);

    public void OnDeviceDisconnected(UsbDeviceInfo device, bool showAllDevices)
    {
        _markerVolumeProbe.Cancel(device);

        BootloaderDevice? bd;
        int remaining;
        CancellationTokenSource? abandon = null;
        lock (_stateLock)
        {
            bd = _bootloaders.FirstOrDefault(b => b.Device == device);
            if (bd != null)
                _bootloaders.Remove(bd);
            remaining = _bootloaders.Count;
            if (bd != null && bd == _operationDevice)
                abandon = _operationCts;
        }

        try
        {
            // Outside the lock: a cancellation callback can run inline on this thread, and the
            // operation may have finished and disposed the source between leaving the lock and here.
            abandon?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (bd != null)
        {
            Output($"{bd.Name} device disconnected{WindowsDriverSuffix(bd)}: {bd}", MessageType.Bootloader);
        }
        else if (showAllDevices)
        {
            Output($"USB device disconnected{WindowsDriverSuffix(device)}: {device}", MessageType.Usb);
        }

        string prefix = $"[ORCH-] {device.TraceVidPid} path:{device.TracePath}";
        if (bd != null)
            Output($"{prefix} -> matched  (bootloaders:{remaining})", MessageType.Debug);
        else if (remaining > 0)
            Output($"{prefix} -> *** no match  (bootloaders:{remaining} - possible phantom entry)", MessageType.Debug);
        else
            Output($"{prefix} -> not a tracked bootloader  (bootloaders:0)", MessageType.Debug);

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

        var cts = new CancellationTokenSource();
        lock (_stateLock)
            _operationCts = cts;

        StateChanged?.Invoke();
        try
        {
            await operation().ConfigureAwait(false);
            return true;
        }
        finally
        {
            lock (_stateLock)
            {
                _operationCts = null;
                _operationDevice = null;
            }
            cts.Dispose();
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

    /// <summary>
    /// Marks <paramref name="device"/> as the operation's cancellation subject while
    /// <paramref name="step"/> runs, so removing the device abandons the step.
    /// </summary>
    private async Task ForDeviceAsync(BootloaderDevice device, Func<Task> step)
    {
        lock (_stateLock)
        {
            _operationDevice = device;
            device.OperationToken = _operationCts?.Token ?? CancellationToken.None;
        }
        try
        {
            await step().ConfigureAwait(false);
        }
        finally
        {
            lock (_stateLock)
            {
                _operationDevice = null;
                device.OperationToken = CancellationToken.None;
            }
        }
    }

    private async Task FlashAllCoreAsync(string mcu, string firmwarePath)
    {
        Output($"[FLASH] FlashAllAsync start  (bootloaders:{BootloaderCount})", MessageType.Debug);
        try
        {
            foreach (BootloaderDevice b in SnapshotBootloaders())
            {
                try
                {
                    Output("Attempting to flash, please don't remove device", MessageType.Bootloader);
                    await ForDeviceAsync(b, () => b.FlashAsync(mcu, firmwarePath)).ConfigureAwait(false);
                    Output("Flash complete", MessageType.Bootloader);
                }
                catch (Exception ex) when (ex is UnsupportedFileFormatException or ComPortNotFoundException)
                {
                    Output(ex.Message, MessageType.Error);
                }
            }
        }
        finally
        {
            Output($"[FLASH] FlashAllAsync finally  (bootloaders:{BootloaderCount})", MessageType.Debug);
        }
    }

    private async Task ResetAllCoreAsync(string mcu)
    {
        Output($"[RESET] ResetAllAsync start  (bootloaders:{BootloaderCount})", MessageType.Debug);
        foreach (BootloaderDevice b in SnapshotBootloaders().Where(b => b.IsResettable))
        {
            try
            {
                await ForDeviceAsync(b, () => b.ResetAsync(mcu)).ConfigureAwait(false);
            }
            catch (ComPortNotFoundException ex)
            {
                Output(ex.Message, MessageType.Error);
            }
        }
    }

    private async Task FlashEepromCoreAsync(string mcu, string fileName, string startMessage, string completeMessage)
    {
        foreach (BootloaderDevice b in SnapshotBootloaders().Where(b => b.IsEepromFlashable))
        {
            try
            {
                Output(startMessage, MessageType.Bootloader);
                await ForDeviceAsync(b, () => b.FlashEepromAsync(mcu, fileName)).ConfigureAwait(false);
                Output(completeMessage, MessageType.Bootloader);
            }
            catch (ComPortNotFoundException ex)
            {
                Output(ex.Message, MessageType.Error);
            }
        }
    }

    private void Output(string message, MessageType type) => services.Output(message, type);

    // Only the Windows probe reports drivers, so without this gate every line on Linux and
    // macOS would read "(NO DRIVER)".
    private static string WindowsDriverSuffix(string label) =>
        IsWindows ? $" ({(label.Length == 0 ? "NO DRIVER" : label)})" : "";

    private static string WindowsDriverSuffix(UsbDeviceInfo device) =>
        WindowsDriverSuffix(Join(device.Drivers));

    private static string WindowsDriverSuffix(BootloaderDevice device) =>
        WindowsDriverSuffix(device.Drivers.Contains(device.PreferredDriver)
            ? device.PreferredDriver
            : Join(device.Drivers));

    // The set has no order of its own, so sort for a log line that reads the same each run.
    private static string Join(IReadOnlySet<string> drivers) =>
        string.Join(", ", drivers.Order(StringComparer.Ordinal));

    public void Dispose()
    {
        _operationGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
