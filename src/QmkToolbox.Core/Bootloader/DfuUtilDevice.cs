using System.Globalization;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Bootloader device for the dfu-util families (<see cref="DfuUtilBootloader.All"/>); accepts
/// only .bin firmware files. The <c>-d</c> device ID is the arriving device's own VID/PID.
/// </summary>
internal sealed class DfuUtilDevice : BootloaderDevice
{
    private readonly DfuUtilBootloader _family;

    public DfuUtilDevice(DfuUtilBootloader family, UsbDeviceInfo device, BootloaderServices services)
        : base(device, services)
    {
        _family = family;
        Type = family.Type;
        Name = family.Name;
        PreferredDriver = "WinUSB";
        IsResettable = family.ResetSuffix != null;
    }

    private string DeviceId => $"{VendorId:X4}:{ProductId:X4}";

    public override Task FlashAsync(string mcu, string file)
    {
        ValidateFileExtension(file, ".bin");

        string[] args = ["-a", _family.AltSetting.ToString(CultureInfo.InvariantCulture), "-d", DeviceId, .. _family.FlashSuffix ?? [], "-D", file];
        return RunToolAsync("dfu-util", args);
    }

    // Only reachable when IsResettable (the orchestrator filters on it); a null suffix
    // degrades to a plain dfu-util invocation.
    public override Task ResetAsync(string mcu)
    {
        string[] args = ["-a", _family.AltSetting.ToString(CultureInfo.InvariantCulture), "-d", DeviceId, .. _family.ResetSuffix ?? []];
        return RunToolAsync("dfu-util", args);
    }
}
