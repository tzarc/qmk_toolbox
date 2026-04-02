using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>Raspberry Pi RP2040/RP2350 BOOTSEL bootloader device (via picotool).</summary>
internal sealed class PicotoolDevice : BootloaderDevice
{
    public PicotoolDevice(IUsbDevice device, IFlashToolProvider toolProvider, bool isRp2350 = false)
        : base(device, toolProvider)
    {
        Type = BootloaderType.Picotool;
        Name = isRp2350 ? "Picotool (RP2350)" : "Picotool (RP2040)";
        PreferredDriver = "WinUSB";
        IsResettable = true;
    }

    public override async Task Flash(string mcu, string file)
    {
        ValidateFileExtension(file, ".uf2", ".bin");
        await RunToolAsync("picotool", "load", file).ConfigureAwait(false);
        await RunToolAsync("picotool", "reboot").ConfigureAwait(false);
    }

    public override Task Reset(string mcu) =>
        RunToolAsync("picotool", "reboot");
}
