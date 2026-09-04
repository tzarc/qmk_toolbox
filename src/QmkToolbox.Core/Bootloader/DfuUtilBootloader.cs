using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// A dfu-util-flashed bootloader family as one row: the alt setting and the extra flash/reset
/// arguments are all that vary between families. The dfu-util <c>-d</c> device ID is not a
/// column; <see cref="DfuUtilDevice"/> derives it from the arriving device's own VID/PID,
/// which is the pair the factory matched on.
/// </summary>
internal sealed record DfuUtilBootloader(
    BootloaderType Type,
    string Name,
    int AltSetting,
    string[]? FlashSuffix,
    string[]? ResetSuffix)
{
    private static readonly string[] DfuseLeave = ["-s", "0x08000000:leave"];

    public static readonly DfuUtilBootloader[] All =
    [
        new(BootloaderType.Apm32Dfu, "APM32 DFU", AltSetting: 0, DfuseLeave, DfuseLeave),
        new(BootloaderType.At32Dfu, "ArteryTek AT32 DFU", AltSetting: 0, DfuseLeave, DfuseLeave),
        new(BootloaderType.Gd32VDfu, "GD32V DFU", AltSetting: 0, DfuseLeave, DfuseLeave),
        new(BootloaderType.KiibohdDfu, "Kiibohd DFU", AltSetting: 0, FlashSuffix: null, ResetSuffix: ["-e"]),
        new(BootloaderType.Stm32Dfu, "STM32 DFU", AltSetting: 0, DfuseLeave, DfuseLeave),
        new(BootloaderType.Stm32Duino, "STM32Duino", AltSetting: 2, FlashSuffix: ["-R"], ResetSuffix: null),
    ];

    public static DfuUtilBootloader For(BootloaderType type) =>
        All.FirstOrDefault(f => f.Type == type)
        ?? throw new ArgumentOutOfRangeException(nameof(type), type, "Not a dfu-util bootloader type");
}
