using QmkToolbox.Core.Models;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>Atmel SAM-BA bootloader device (Massdrop, via mdloader).</summary>
internal sealed class AtmelSamBaDevice : BootloaderDevice
{
    public AtmelSamBaDevice(UsbDeviceInfo device, BootloaderServices services)
        : base(device, services, resolvesComPort: true)
    {
        Type = BootloaderType.AtmelSamBa;
        Name = "Atmel SAM-BA";
        PreferredDriver = "usbser";
        IsResettable = true;
    }

    public override async Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".bin");
        string port = RequireComPort(ComPortTask is { } t ? await t.ConfigureAwait(false) : null);
        await RunToolAsync("mdloader", "-p", port, "-D", file, "--restart");
    }

    public override async Task ResetAsync(string mcu)
    {
        string port = RequireComPort(ComPortTask is { } t ? await t.ConfigureAwait(false) : null);
        await RunToolAsync("mdloader", "-p", port, "--restart");
    }
}
