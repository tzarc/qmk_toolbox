# QmkToolbox.Usb.Abstractions

The contracts shared by [QmkToolbox.Usb.Discovery](../QmkToolbox.Usb.Discovery/README.md) and [QmkToolbox.Usb.Hid](../QmkToolbox.Usb.Hid/README.md): the `UsbDeviceInfo` device snapshot, the `IUsbEventsDetector` hotplug contract, and the `HidInterfaceInfo`/`HidInterfaceDevice` HID interface surface.

Reference an implementation library rather than this one; each brings these contracts with it. Types keep the namespaces of the libraries that deliver them (`QmkToolbox.Usb.Discovery`, `QmkToolbox.Usb.Hid`), so code written against the implementations compiles unchanged.
