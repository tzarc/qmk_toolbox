using Qmk.Usb.Discovery;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Base class for bootloader devices that use avrdude for flashing.
/// Subclasses provide the programmer name, driver, and COM port requirements.
/// </summary>
internal abstract class AvrdudeDevice : BootloaderDevice
{
    private readonly string _programmer;

    protected AvrdudeDevice(
        UsbDeviceInfo device,
        BootloaderServices services,
        BootloaderType type,
        string name,
        string programmer,
        string preferredDriver,
        bool requiresComPort,
        bool isEepromFlashable)
        : base(device, services, requiresComPort)
    {
        Type = type;
        Name = name;
        PreferredDriver = preferredDriver;
        IsEepromFlashable = isEepromFlashable;
        _programmer = programmer;
    }

    private async Task RunAsync(string mcu, string target, string file)
    {
        // ArgumentList passes the whole -U value ("flash:w:/path/to/file:i") as one argument, so it needs no manual quoting.
        string[] port = ComPortTask == null ? [] : ["-P", RequireComPort(await ComPortTask.ConfigureAwait(false))];
        await RunToolAsync("avrdude", ["-p", mcu, "-c", _programmer, "-U", $"{target}:w:{file}:i", .. port]);
    }

    public override async Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".hex");
        await RunAsync(mcu, "flash", file);
    }

    public override async Task FlashEepromAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".eep", ".hex");
        await RunAsync(mcu, "eeprom", file);
    }
}
