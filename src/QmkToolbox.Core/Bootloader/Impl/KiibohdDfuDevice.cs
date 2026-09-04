using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>Kiibohd DFU bootloader device (Input Club, via dfu-util).</summary>
internal sealed class KiibohdDfuDevice(IUsbDevice device, BootloaderServices services)
    : DfuUtilDevice(device, services,
        BootloaderType.KiibohdDfu, "Kiibohd DFU",
        altSetting: 0, deviceId: "1C11:B007",
        flashSuffix: null,
        resetSuffix: ["-e"]);
