using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Startup-banner catalogue of supported bootloaders and ISP flashers. Each row names the
/// <see cref="BootloaderType"/>s it covers; an exhaustiveness test fails when a type has no banner row.
/// </summary>
public static class BootloaderBanner
{
    public sealed record Entry(BootloaderType[] Types, string Line);

    public static readonly Entry[] Bootloaders =
    [
        new([
                BootloaderType.Apm32Dfu, BootloaderType.At32Dfu, BootloaderType.KiibohdDfu,
                BootloaderType.Stm32Dfu, BootloaderType.Stm32Duino, BootloaderType.Gd32VDfu,
            ],
            "ARM DFU (APM32, AT32, Kiibohd, STM32, STM32duino) and RISC-V DFU (GD32V) via dfu-util (http://dfu-util.sourceforge.net/)"),
        new([BootloaderType.AtmelSamBa],
            "Atmel SAM-BA (Massdrop) via Massdrop Loader (https://github.com/massdrop/mdloader)"),
        new([BootloaderType.AtmelDfu, BootloaderType.QmkDfu],
            "Atmel/LUFA/QMK DFU via dfu-programmer (http://dfu-programmer.github.io/)"),
        new([BootloaderType.BootloadHid],
            "BootloadHID (Atmel, PS2AVRGB) via bootloadHID (https://www.obdev.at/products/vusb/bootloadhid.html)"),
        new([BootloaderType.Caterina],
            "Caterina (Arduino, Pro Micro) via avrdude (http://nongnu.org/avrdude/)"),
        new([BootloaderType.HalfKay],
            "HalfKay (Teensy, Ergodox EZ) via Teensy Loader (https://pjrc.com/teensy/loader_cli.html)"),
        new([BootloaderType.LufaMs],
            "LUFA Mass Storage"),
        new([BootloaderType.LufaHid, BootloaderType.QmkHid],
            "LUFA/QMK HID via hid_bootloader_cli (https://github.com/abcminiuser/lufa)"),
        new([BootloaderType.Picotool],
            "Raspberry Pi RP2040/RP2350 (BOOTSEL) via picotool (https://github.com/raspberrypi/picotool)"),
        new([BootloaderType.Uf2],
            "UF2 Mass Storage (https://github.com/microsoft/uf2)"),
        new([BootloaderType.Wb32Dfu],
            "WB32 DFU via wb32-dfu-updater_cli (https://github.com/WestberryTech/wb32-dfu-updater)"),
    ];

    public static readonly Entry[] IspFlashers =
    [
        new([BootloaderType.AvrIsp], "AVRISP (Arduino ISP)"),
        new([BootloaderType.UsbAsp], "USBasp (AVR ISP)"),
        new([BootloaderType.UsbTinyIsp], "USBTiny (AVR Pocket)"),
    ];
}
