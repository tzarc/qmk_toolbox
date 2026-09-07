# QmkToolbox.Usb.Hid

HID interface enumeration and report I/O for Windows, Linux, and macOS, built on [hidapi](https://github.com/libusb/hidapi). The native hidapi library ships from `resources/hidapi` next to the executable; `HidApi.Net`'s resolver finds it there.

## Enumerating interfaces

`EnumerateHidInterfaces` lists every connected HID interface, one entry per top-level collection, so interfaces are selectable by usage page and usage. Exclusively held interfaces (regular keyboards, mice) still appear; they only refuse opening.

```csharp
using QmkToolbox.Usb.Hid;

HidInterfaceInfo? console = UsbHidInterfaces.EnumerateHidInterfaces()
    .FirstOrDefault(i => i.UsagePage == 0xFF31 && i.Usage == 0x0074);
```

With `QmkToolbox.Usb.Discovery` tracking devices, the device-scoped overload resolves the interfaces one device backs. The lookup anchors to the device instance, so two identical devices never see each other's interfaces:

```csharp
HidInterfaceInfo? console = device.EnumerateHidInterfaces()
    .FirstOrDefault(i => i.UsagePage == 0xFF31 && i.Usage == 0x0074);
```

Enumeration is a snapshot; there are no hotplug callbacks. Poll it, or drive it from `UsbDeviceTracker`'s connect events.

## Reading and writing reports

`Open` returns a `HidInterfaceDevice` that raises input reports as events. Subscribe, then call `Start`:

```csharp
using HidInterfaceDevice? hid = console?.Open();
if (hid is not null)
{
    hid.ReportReceived += payload => Console.WriteLine(Convert.ToHexString(payload));
    hid.Closed += () => Console.WriteLine("interface gone");
    hid.Start();
}
```

Reports arrive on the device's read thread; marshal in your handler. `Write` sends one output report. Payloads carry no report-ID byte in either direction for interfaces that use none, so the same bytes move on every platform.

## Requirements

.NET 10 on Windows, Linux, or macOS 13+. The hidapi builds ship as `qmk_hidapi-*` archives in [qmk_flashutils](https://github.com/qmk/qmk_flashutils/releases) releases. On Linux, opening a device requires read/write access to its `/dev/hidraw*` node (a udev rule, typically).
