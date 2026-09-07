using QmkToolbox.Core.Models;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>HalfKay bootloader device (Teensy/Ergodox EZ, via teensy_loader_cli).</summary>
internal sealed class HalfKayDevice : BootloaderDevice
{
    public HalfKayDevice(UsbDeviceInfo device, BootloaderServices services)
        : base(device, services)
    {
        Type = BootloaderType.HalfKay;
        Name = "HalfKay";
        PreferredDriver = "HidUsb";
        IsResettable = true;
    }

    public override Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".hex");
        return RunToolAsync("teensy_loader_cli", $"-mmcu={mcu}", file, "-v");
    }

    public override Task ResetAsync(string mcu) =>
        RunToolAsync("teensy_loader_cli", $"-mmcu={mcu}", "-bv");
}
