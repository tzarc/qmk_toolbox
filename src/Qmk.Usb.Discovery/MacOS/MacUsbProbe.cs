using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Qmk.Usb.Discovery.MacOS;

/// <summary>
/// macOS probe using IOKit matching notifications on a dedicated CFRunLoop thread; no polling
/// and no external watcher library. Arrivals are enriched straight off the arriving
/// io_service_t (identity, revision, strings, registry path); terminations carry identity and
/// path for the tracker's removal matching. The initial notification drain delivers devices
/// already present at registration; the startup sweep overlaps it and the tracker's
/// duplicate-path guard drops the copies (both sides derive the path from
/// IORegistryEntryGetPath, so the paths are identical).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacUsbProbe : IUsbProbe
{
    private const string IOKitLib = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint KCfStringEncodingUtf8 = 0x08000100;

    private delegate void IOServiceMatchingCallback(IntPtr refCon, IntPtr iterator);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IONotificationPortCreate(IntPtr mainPort);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern void IONotificationPortDestroy(IntPtr port);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IONotificationPortGetRunLoopSource(IntPtr port);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr IOServiceMatching(string name);

    [DllImport(IOKitLib, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int IOServiceAddMatchingNotification(
        IntPtr port, string notificationType, IntPtr matching,
        IOServiceMatchingCallback callback, IntPtr refCon, out IntPtr iterator);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern IntPtr IOIteratorNext(IntPtr iterator);

    [DllImport(IOKitLib, ExactSpelling = true)]
    private static extern int IOObjectRelease(IntPtr obj);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern void CFRunLoopRun();

    [DllImport(CoreFoundationLib, ExactSpelling = true)]
    private static extern void CFRunLoopStop(IntPtr runLoop);

    [DllImport(CoreFoundationLib, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    public event Action<UsbDeviceInfo>? Arrived;
    public event Action<UsbRemovalHint>? Removed;

    public StringComparison PathComparison => StringComparison.Ordinal;

    // Kept as fields: the delegates must outlive the unmanaged notification registrations.
    private IOServiceMatchingCallback? _arrivalCallback;
    private IOServiceMatchingCallback? _terminationCallback;
    private Thread? _runLoopThread;
    private IntPtr _port;
    private IntPtr _arrivalIterator;
    private IntPtr _terminationIterator;
    private IntPtr _runLoop;
    private readonly ManualResetEventSlim _runLoopReady = new(false);

    public void Start()
    {
        _port = IONotificationPortCreate(IntPtr.Zero);
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
        IntPtr mode = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", KCfStringEncodingUtf8);
        CFRunLoopAddSource(_runLoop, IONotificationPortGetRunLoopSource(_port), mode);
        // Draining arms the notifications; the arrival drain also delivers devices already
        // present at registration, completed before Start() returns so the tracker's sweep
        // runs against an armed subscription.
        DrainArrivals(_arrivalIterator);
        DrainTerminations(_terminationIterator);
        _runLoopReady.Set();
        CFRunLoopRun();
    }

    private void OnArrivalNotification(IntPtr refCon, IntPtr iterator) => DrainArrivals(iterator);

    private void OnTerminationNotification(IntPtr refCon, IntPtr iterator) => DrainTerminations(iterator);

    private void DrainArrivals(IntPtr iterator)
    {
        IntPtr service;
        while ((service = IOIteratorNext(iterator)) != IntPtr.Zero)
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
                IOObjectRelease(service);
            }
        }
    }

    private void DrainTerminations(IntPtr iterator)
    {
        IntPtr service;
        while ((service = IOIteratorNext(iterator)) != IntPtr.Zero)
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
                IOObjectRelease(service);
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
        if (_arrivalIterator != IntPtr.Zero)
        {
            IOObjectRelease(_arrivalIterator);
            _arrivalIterator = IntPtr.Zero;
        }
        if (_terminationIterator != IntPtr.Zero)
        {
            IOObjectRelease(_terminationIterator);
            _terminationIterator = IntPtr.Zero;
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
