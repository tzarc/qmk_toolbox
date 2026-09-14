using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Shared dependencies for the bootloader device family, built once and handed unchanged to
/// every device the factory creates. Devices pull what they need; families without a serial
/// port or mount point never call the resolver.
/// </summary>
public sealed record BootloaderServices(IFlashToolProvider ToolProvider, IUsbDeviceResolver UsbResolver, MessageSink Output)
{
    /// <summary>Process launcher for flash tools; a fake lets tests capture commands without forking.</summary>
    public IProcessRunner ProcessRunner { get; init; } = SystemProcessRunner.Shared;

    /// <summary>Clock for the flash-tool timeout; a fake triggers it deterministically.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Delay between serial-port and mount-point resolution attempts.</summary>
    public int PollDelayMs { get; init; } = 250;

    /// <summary>Delay between marker-volume probes of an unrecognised mass-storage device.</summary>
    public int VolumeProbeDelayMs { get; init; } = 250;
}
