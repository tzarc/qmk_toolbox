using QmkToolbox.Core.Services;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Shared dependencies for the bootloader device family, built once and handed unchanged to
/// every device the factory creates. Devices pull what they need; families without a serial
/// port or mount point never touch those members.
/// </summary>
public sealed record BootloaderServices(IFlashToolProvider ToolProvider)
{
    /// <summary>Process launcher for flash tools; a fake lets tests capture commands without forking.</summary>
    public IProcessRunner ProcessRunner { get; init; } = SystemProcessRunner.Shared;

    /// <summary>Clock for the flash-tool timeout; a fake triggers it deterministically.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Delay between serial-port and mount-point resolution attempts.</summary>
    public int PollDelayMs { get; init; } = 250;

    public ISerialPortService? SerialPorts { get; init; }

    public IMountPointService? MountPoints { get; init; }
}
