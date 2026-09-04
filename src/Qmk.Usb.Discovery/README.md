# Qmk.Usb.Discovery

Cross-platform USB device hotplug detection for .NET, with no polling and no bundled native libraries. Each platform uses its native notification mechanism directly:

| Platform | Backend                                                            |
| -------- | ------------------------------------------------------------------ |
| Windows  | `RegisterDeviceNotification` via a message-only window (no WMI)    |
| Linux    | Raw netlink kobject uevents from the kernel (no udevd, no libudev) |
| macOS    | IOKit matching notifications on a dedicated CFRunLoop thread       |

Requires .NET 10. Supported on Windows, Linux, and macOS 13+. The package covers detection, identity, and volume topology; device I/O is out of scope.

## Quick start

```csharp
using Qmk.Usb.Discovery;

using var detector = new UsbDeviceTracker(UsbProbe.CreateForCurrentPlatform());

detector.DeviceConnected += device =>
    Console.WriteLine($"connected: {device}  path: {device.DevicePath}");
detector.DeviceDisconnected += device =>
    Console.WriteLine($"disconnected: {device}");

detector.Start();
// ... run your application ...
detector.Stop();
```

`Start()` first delivers every device already connected (the startup sweep), then live arrivals and removals. `Start()` throws if the platform's notification mechanism cannot be set up; detection would be silently dead otherwise, so surface the failure to your user.

## Semantics

- **Arrivals are fully enriched.** `DeviceConnected` delivers a `UsbDeviceInfo` carrying VID/PID, `bcdDevice` revision, manufacturer/product strings, the platform's device path, the OS driver name where available, and whether the device exposes a mass-storage interface.
- **Removals preserve identity.** `DeviceDisconnected` always delivers the *identical* `UsbDeviceInfo` instance previously delivered by `DeviceConnected`, so consumers may key state by reference and compare devices with plain `==`. Platforms report removals inconsistently (a path, a VID/PID pair, or both); the tracker resolves that back to the tracked arrival internally.
- **Duplicates are suppressed.** A device delivered by both the startup sweep and a racing notification is reported once. USB hubs are filtered out.
- **Threading:** events are raised on the detection thread, not your UI thread, so marshal in your handler. Assigning `DiagnosticTrace` streams matching/sweep decisions (`[USB+]`/`[USB-]` lines) for troubleshooting, on the same thread.

## Testing consumers

Both seams are public, so code built on this library never needs real hardware in tests:

- Fake `IUsbEventsDetector` to drive your application logic with scripted connect/disconnect events. Honour the identity invariant: pass the same instance to disconnect that you passed to connect.
- Fake `IUsbProbe` and wrap it in a real `UsbDeviceTracker` to exercise tracking, dedup, sweep, and removal-matching behaviour exactly as production runs it.

```csharp
var probe = new FakeProbe();                    // your IUsbProbe test double
using var detector = new UsbDeviceTracker(probe);
detector.Start();
probe.RaiseArrived(new UsbDeviceInfo(0x2E8A, 0x0003, 0x0100, "Raspberry Pi", "RP2 Boot", "", "/sys/devices/usb1/1-2"));
```

## Volume ownership

For mass-storage devices, the library answers "is this mounted volume backed by this USB device?", useful for rejecting look-alike volumes on unrelated storage devices:

```csharp
bool? owned = device.OwnsVolume("/media/user/RPI-RP2");
// true/false when provable; null when the platform cannot say;
// treat null as acceptable rather than rejecting a working volume.
```

`mountPoint` is the volume's mount point in the platform's own form (`E:\`, `/media/user/VOLUME`, `/Volumes/VOLUME`); each platform resolves it to the owning USB device internally (drive → disk → parent devnode on Windows, statfs → IOMedia → registry parents on macOS, mount source → sysfs block-device ancestry on Linux).

## Utilities

- `DeviceTrace`: the shared formatting grammar for device trace lines (`VID:XXXX PID:XXXX`, quoted paths), so diagnostics stay greppable across producers.
