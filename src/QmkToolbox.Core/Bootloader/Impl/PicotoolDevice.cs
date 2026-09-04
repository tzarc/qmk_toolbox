using Qmk.Usb.Discovery;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>Raspberry Pi BOOTSEL bootloader device (via picotool).</summary>
internal sealed class PicotoolDevice : BootloaderDevice
{
    public PicotoolDevice(UsbDeviceInfo device, BootloaderServices services)
        : base(device, services)
    {
        string model = device.ProductId switch
        {
            0x0003 => "RP2040",
            0x000F => "RP2350",
            _ => $"0x{device.ProductId:X4}",
        };
        Type = BootloaderType.Picotool;
        Name = $"Picotool ({model})";
        PreferredDriver = "WinUSB";
        IsResettable = true;
    }

    public override async Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".uf2", ".bin");
        await RunToolAsync("picotool", "load", file).ConfigureAwait(false);
        await RunToolAsync("picotool", "reboot").ConfigureAwait(false);
    }

    public override Task ResetAsync(string mcu) =>
        RunToolAsync("picotool", "reboot");
}
