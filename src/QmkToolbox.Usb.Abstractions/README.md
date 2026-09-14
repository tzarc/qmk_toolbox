# QmkToolbox.Usb.Abstractions

The contracts shared by [QmkToolbox.Usb.Discovery](../QmkToolbox.Usb.Discovery/README.md) and [QmkToolbox.Usb.Hid](../QmkToolbox.Usb.Hid/README.md): the `UsbDeviceInfo` device snapshot, the `IUsbEventsDetector` hotplug contract, the `IUsbDeviceResolver` serial-port and volume contract, and the `HidInterfaceInfo`/`HidInterfaceDevice` HID interface surface.

Reference an implementation library rather than this one; each brings these contracts with it. The types keep the namespaces of the libraries that deliver them (`QmkToolbox.Usb.Discovery`, `QmkToolbox.Usb.Hid`); this package adds no namespace of its own.
