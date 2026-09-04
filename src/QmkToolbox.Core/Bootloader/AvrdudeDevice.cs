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
        IUsbDevice device,
        BootloaderServices services,
        BootloaderType type,
        string name,
        string programmer,
        string preferredDriver,
        bool requiresComPort,
        bool isEepromFlashable)
        : base(device, services)
    {
        Type = type;
        Name = name;
        PreferredDriver = preferredDriver;
        IsEepromFlashable = isEepromFlashable;
        _programmer = programmer;
        // Port resolution starts immediately on device connect and runs in the background;
        // all operations await the same Task, so resolution happens at most once.
        ComPortTask = requiresComPort ? FindComPortAsync() : null;
    }

    private async Task RunAsync(string mcu, string target, string file)
    {
        // The -U value is a single argument: "flash:w:/path/to/file:i"
        // ArgumentList passes it as one discrete argument, so no manual quoting is needed.
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
