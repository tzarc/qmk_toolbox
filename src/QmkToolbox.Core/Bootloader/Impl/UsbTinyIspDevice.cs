using Qmk.Usb.Discovery;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Bootloader.Impl;

/// <summary>USBtinyISP flasher device (via avrdude with usbtiny programmer).</summary>
internal sealed class UsbTinyIspDevice(UsbDeviceInfo device, BootloaderServices services)
    : AvrdudeDevice(device, services,
        BootloaderType.UsbTinyIsp, "USBtinyISP", programmer: "usbtiny",
        preferredDriver: "libusb0", requiresComPort: false, isEepromFlashable: true);
