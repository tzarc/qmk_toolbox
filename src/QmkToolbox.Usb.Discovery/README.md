# QmkToolbox.Usb.Discovery

USB device hotplug detection for .NET on Windows, Linux, and macOS: subscribe to connect and disconnect events, pick up the devices already attached at startup, and resolve the serial ports and mounted volumes a device backs.

## Quick start

```csharp
using QmkToolbox.Usb.Discovery;

using var detector = new UsbDeviceTracker();

detector.DeviceConnected += device =>
    Console.WriteLine($"connected: {device}  path: {device.DevicePath}");
detector.DeviceDisconnected += device =>
    Console.WriteLine($"disconnected: {device}");

detector.Start();
// ... run your application ...
detector.Stop();
```

`Start()` first delivers every device already connected, then live arrivals and removals. It throws if monitoring cannot start, and swallowing that leaves detection dead for the rest of the session.

## Working with devices

- `DeviceConnected` delivers a `UsbDeviceInfo` snapshot: VID/PID, `bcdDevice` revision, manufacturer and product strings, the platform's device path, the OS driver name where available, and whether the device exposes a mass-storage interface.
- `DeviceDisconnected` delivers the same instance `DeviceConnected` did, so key your state by reference and compare devices with `==`. The detector reports each device once and filters out USB hubs.
- The detector raises events on its own thread, not your UI thread, so marshal in your handler.
- Assign `DiagnosticTrace` to receive the detector's trace lines (`[USB+]`/`[USB-]`) when troubleshooting.

## Serial ports and volumes

Extensions on `UsbDeviceInfo` resolve what the OS attached for a device's CDC-ACM and mass-storage functions:

```csharp
IEnumerable<string> ports = device.EnumerateSerialPorts(); // "COM3", "/dev/ttyACM0", "/dev/cu.usbmodem1101"
IEnumerable<string> mounts = device.EnumerateVolumes();    // "E:\", "/media/user/VOLUME", "/Volumes/VOLUME"
```

`EnumerateSerialPorts` yields every port a multi-interface device exposes, primary interface first. `EnumerateVolumes` yields only mount points the device provably backs. Ports and mounts appear some time after the connect event, so retry on an empty result.

`UsbDeviceResolver` puts the same pair behind an interface, for callers that resolve through injected dependencies:

```csharp
IUsbDeviceResolver resolver = new UsbDeviceResolver();
IEnumerable<string> ports = resolver.EnumerateSerialPorts(device);
```

## Volume ownership

For mass-storage devices, `OwnsVolume` answers "is this mounted volume backed by this USB device?", so a look-alike volume on unrelated storage can be rejected:

```csharp
bool? owned = device.OwnsVolume("/media/user/RPI-RP2");
// true/false when provable; null when the platform cannot say;
// treat null as acceptable rather than rejecting a working volume.
```

`mountPoint` takes the platform's own form: `E:\`, `/media/user/VOLUME`, or `/Volumes/VOLUME`.

## How it works

Each platform uses its native notification mechanism directly, so there is no polling and no bundled native library:

| Platform | Backend                                                            |
| -------- | ------------------------------------------------------------------ |
| Windows  | `RegisterDeviceNotification` via a message-only window (no WMI)    |
| Linux    | Raw netlink kobject uevents from the kernel (no udevd, no libudev) |
| macOS    | IOKit matching notifications on a dedicated CFRunLoop thread       |

Volume ownership resolves the mount point to its owning USB device through each platform's storage stack: the device tree on Windows, IOKit on macOS, and sysfs on Linux.

## Requirements

.NET 10 on Windows, Linux, or macOS 13+. The library covers detection, identity, serial ports, and volume topology; device I/O is out of scope.
