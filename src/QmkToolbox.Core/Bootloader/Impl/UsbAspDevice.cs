using QmkToolbox.Core.Models;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>USBasp ISP flasher device (via avrdude with usbasp programmer).</summary>
internal sealed class UsbAspDevice(UsbDeviceInfo device, BootloaderServices services)
    : AvrdudeDevice(device, services,
        BootloaderType.UsbAsp, "USBasp", programmer: "usbasp",
        preferredDriver: "libusbK", requiresComPort: false, isEepromFlashable: true);
