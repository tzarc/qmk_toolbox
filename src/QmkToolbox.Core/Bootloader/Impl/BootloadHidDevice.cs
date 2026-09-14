using QmkToolbox.Core.Models;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>BootloadHID bootloader device (Atmel/PS2AVRGB, via bootloadHID).</summary>
internal sealed class BootloadHidDevice : BootloaderDevice
{
    public BootloadHidDevice(UsbDeviceInfo device, BootloaderServices services)
        : base(device, services)
    {
        Type = BootloaderType.BootloadHid;
        Name = "BootloadHID";
        PreferredDriver = "HidUsb";
        IsResettable = true;
    }

    public override Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".hex");
        return RunToolAsync("bootloadHID", "-r", file);
    }

    public override Task ResetAsync(string mcu) =>
        RunToolAsync("bootloadHID", "-r");
}
