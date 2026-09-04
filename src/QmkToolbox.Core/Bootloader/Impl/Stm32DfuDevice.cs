using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>STM32 DFU bootloader device (STMicroelectronics, via dfu-util).</summary>
internal sealed class Stm32DfuDevice(IUsbDevice device, BootloaderServices services)
    : DfuUtilDevice(device, services,
        BootloaderType.Stm32Dfu, "STM32 DFU",
        altSetting: 0, deviceId: "0483:DF11",
        flashSuffix: ["-s", "0x08000000:leave"],
        resetSuffix: ["-s", "0x08000000:leave"]);
