using Qmk.Usb.Discovery;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>AVR ISP bootloader device (via avrdude with avrisp programmer).</summary>
internal sealed class AvrIspDevice(UsbDeviceInfo device, BootloaderServices services)
    : AvrdudeDevice(device, services,
        BootloaderType.AvrIsp, "AVR ISP", programmer: "avrisp",
        preferredDriver: "usbser", requiresComPort: true, isEepromFlashable: false);
