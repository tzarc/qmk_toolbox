using System.Runtime.Versioning;

using static Interop.CoreFoundation;
using static Interop.IOKit;

namespace QmkToolbox.Usb.Discovery.MacOS;

/// <summary>
/// macOS probe: IOKit matching notifications on a dedicated CFRunLoop thread, with no polling
/// and no external watcher library. Arrivals are enriched straight off the arriving
/// <c>io_service_t</c>; terminations carry identity and path for the tracker's removal
/// matching. The initial notification drain delivers devices already present at registration;
/// the startup sweep overlaps it and the tracker's duplicate-path guard drops the copies (both
/// sides derive the path from IORegistryEntryGetPath, so the paths are identical).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacUsbProbe : IUsbProbe
{
    public event Action<UsbDeviceInfo>? Arrived;
    public event Action<UsbRemovalHint>? Removed;

    public StringComparison PathComparison => StringComparison.Ordinal;

    // The delegates must outlive the unmanaged notification registrations.
    private IOServiceMatchingCallback? _arrivalCallback;
    private IOServiceMatchingCallback? _terminationCallback;
    private Thread? _runLoopThread;
    private IntPtr _port;
    private uint _arrivalIterator;
    private uint _terminationIterator;
    private IntPtr _runLoop;
    private readonly ManualResetEventSlim _runLoopReady = new(false);

    public void Start()
    {
        _port = IONotificationPortCreate(KIOMainPortDefault);
        if (_port == IntPtr.Zero)
            throw new InvalidOperationException("IOKit notification port creation failed.");

        _arrivalCallback = OnArrivalNotification;
        _terminationCallback = OnTerminationNotification;

        // Each IOServiceMatching dictionary is consumed by its registration. The literals are
        // kIOFirstMatchNotification / kIOTerminatedNotification.
        int kr = IOServiceAddMatchingNotification(_port, "IOServiceFirstMatch",
            IOServiceMatching("IOUSBHostDevice"), _arrivalCallback, IntPtr.Zero, out _arrivalIterator);
        if (kr != 0)
        {
            Stop();
            throw new InvalidOperationException($"USB arrival notification registration failed (IOKit error 0x{kr:X8}).");
        }
        kr = IOServiceAddMatchingNotification(_port, "IOServiceTerminate",
            IOServiceMatching("IOUSBHostDevice"), _terminationCallback, IntPtr.Zero, out _terminationIterator);
        if (kr != 0)
        {
            Stop();
            throw new InvalidOperationException($"USB removal notification registration failed (IOKit error 0x{kr:X8}).");
        }

        _runLoopReady.Reset();
        _runLoopThread = new Thread(RunLoop) { IsBackground = true, Name = "UsbIoKitNotifications" };
        _runLoopThread.Start();
        _runLoopReady.Wait();
    }

    private void RunLoop()
    {
        _runLoop = CFRunLoopGetCurrent();
        IntPtr mode = CFStringCreateWithCString(IntPtr.Zero, KCfRunLoopDefaultMode, KCfStringEncodingUtf8);
        CFRunLoopAddSource(_runLoop, IONotificationPortGetRunLoopSource(_port), mode);
        // Draining arms the notifications, and the arrival drain also delivers devices already
        // present at registration. It completes before Start() returns, so the tracker's sweep
        // runs against an armed subscription.
        DrainArrivals(_arrivalIterator);
        DrainTerminations(_terminationIterator);
        _runLoopReady.Set();
        CFRunLoopRun();
    }

    private void OnArrivalNotification(IntPtr refCon, uint iterator) => DrainArrivals(iterator);

    private void OnTerminationNotification(IntPtr refCon, uint iterator) => DrainTerminations(iterator);

    private void DrainArrivals(uint iterator)
    {
        uint service;
        while ((service = IOIteratorNext(iterator)) != 0)
        {
            try
            {
                if (!MacUsbRegistry.ShouldSkipDevice(service))
                    Arrived?.Invoke(MacUsbRegistry.BuildDeviceInfo(service));
            }
            catch (Exception)
            {
                // A single bad registry entry must never kill the notification thread.
            }
            finally
            {
                _ = IOObjectRelease(service);
            }
        }
    }

    private void DrainTerminations(uint iterator)
    {
        uint service;
        while ((service = IOIteratorNext(iterator)) != 0)
        {
            try
            {
                (ushort vid, ushort pid, string path) = MacUsbRegistry.ReadIdentity(service);
                Removed?.Invoke(new UsbRemovalHint(path, vid, pid));
            }
            catch (Exception)
            {
                // A single bad registry entry must never kill the notification thread.
            }
            finally
            {
                _ = IOObjectRelease(service);
            }
        }
    }

    public void Stop()
    {
        if (_runLoop != IntPtr.Zero)
        {
            CFRunLoopStop(_runLoop);
            _runLoop = IntPtr.Zero;
        }
        _runLoopThread?.Join(TimeSpan.FromSeconds(2));
        _runLoopThread = null;
        if (_arrivalIterator != 0)
        {
            _ = IOObjectRelease(_arrivalIterator);
            _arrivalIterator = 0;
        }
        if (_terminationIterator != 0)
        {
            _ = IOObjectRelease(_terminationIterator);
            _terminationIterator = 0;
        }
        if (_port != IntPtr.Zero)
        {
            IONotificationPortDestroy(_port);
            _port = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();

    public IEnumerable<UsbDeviceInfo> EnumeratePresent() => MacUsbRegistry.EnumeratePresentDevices();
}
