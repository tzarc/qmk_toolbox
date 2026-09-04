using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Base class for bootloader devices that use dfu-util and accept only .bin firmware files.
/// Subclasses provide device-specific parameters (alt setting, USB device ID, flash/reset suffixes).
/// </summary>
internal abstract class DfuUtilDevice : BootloaderDevice
{
    private readonly int _altSetting;
    private readonly string _deviceId;
    private readonly string[] _flashSuffix;
    private readonly string[]? _resetSuffix;

    /// <param name="altSetting">The DFU alt setting (-a flag).</param>
    /// <param name="deviceId">The USB VID:PID string (-d flag), e.g. "0483:DF11".</param>
    /// <param name="flashSuffix">Extra individual args inserted between -d and -D during flash
    /// (e.g. <c>["-s", "0x08000000:leave"]</c>), or null/empty for none.</param>
    /// <param name="resetSuffix">Individual args appended after -d during reset
    /// (e.g. <c>["-s", "0x08000000:leave"]</c> or <c>["-e"]</c>),
    /// or null if reset is not supported.</param>
    protected DfuUtilDevice(
        IUsbDevice device,
        BootloaderServices services,
        BootloaderType type,
        string name,
        int altSetting,
        string deviceId,
        string[]? flashSuffix,
        string[]? resetSuffix)
        : base(device, services)
    {
        Type = type;
        Name = name;
        PreferredDriver = "WinUSB";
        IsResettable = resetSuffix != null;

        _altSetting = altSetting;
        _deviceId = deviceId;
        _flashSuffix = flashSuffix ?? [];
        _resetSuffix = resetSuffix;
    }

    public override Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".bin");

        string[] args = ["-a", _altSetting.ToString(), "-d", _deviceId, .. _flashSuffix, "-D", file];
        return RunToolAsync("dfu-util", args);
    }

    // Only reachable when IsResettable (the orchestrator filters on it); a null suffix
    // degrades to a plain dfu-util invocation rather than guarding a path nothing takes.
    public override Task ResetAsync(string mcu)
    {
        string[] args = ["-a", _altSetting.ToString(), "-d", _deviceId, .. _resetSuffix ?? []];
        return RunToolAsync("dfu-util", args);
    }
}
