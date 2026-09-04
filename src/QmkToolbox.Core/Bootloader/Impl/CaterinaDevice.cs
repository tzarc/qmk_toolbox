using Qmk.Usb.Discovery;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>Caterina bootloader device (Arduino/Pro Micro, via avrdude with avr109 programmer).</summary>
internal sealed class CaterinaDevice(UsbDeviceInfo device, BootloaderServices services)
    : AvrdudeDevice(device, services,
        BootloaderType.Caterina, "Caterina", programmer: "avr109",
        preferredDriver: "usbser", requiresComPort: true, isEepromFlashable: true);
