using System.Diagnostics;
using System.Runtime.InteropServices;

using static Interop.Cfgmgr32;
using static Interop.User32;

namespace QmkToolbox.Usb.Discovery.Windows;

// Not WMI: Win32_PnPEntity event queries delay events behind "WITHIN n" polling, WMI
// initialisation costs ~7 seconds of cold start in a fresh .NET 10 process, and its
// reflection/COM plumbing breaks under PublishTrimmed.

/// <summary>
/// Windows probe using RegisterDeviceNotification via a message-only window. Removal hints carry
/// the interface path only: Windows interface paths are canonical and always present, so the
/// tracker never needs a VID/PID fallback. The probe announces live arrivals only after the
/// devnode settles, so the reported drivers and mass-storage flag reflect the bound driver stack.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsUsbProbe : IUsbProbe
{
    public event Action<UsbDeviceInfo>? Arrived;
    public event Action<UsbRemovalHint>? Removed;

    // Interface paths compare case-insensitively (some paths are reported with differing case).
    public StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;

    // Live arrivals settle on a worker before the probe announces them (SettleAndRaiseAsync);
    // a removal broadcast cancels its pending arrival, so a gone device stays unannounced.
    private readonly Dictionary<string, CancellationTokenSource> _pendingArrivals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _pendingLock = new();
    private const int SettlePollMs = 100;
    private const int SettleTimeoutMs = 3000;

    private Thread? _messageThread;
    private volatile IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _notifyHandle = IntPtr.Zero;
    private int _windowError;
    private int _notifyError;
    private uint _messageThreadId;
    private readonly ManualResetEventSlim _hwndReady = new(false);
    // The delegate must outlive the unmanaged window class registration.
    private WndProcDelegate? _wndProcDelegate;

    // GUID_DEVINTERFACE_USB_DEVICE: broadcasts name root USB device nodes, not composite children.
    private static readonly Guid GuidDevInterfaceUsbDevice =
        new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    public void Start()
    {
        _wndProcDelegate = WndProc;
        _hwndReady.Reset();
        _messageThread = new Thread(MessagePump) { IsBackground = true, Name = "UsbDetectorMessagePump" };
        _messageThread.Start();
        _hwndReady.Wait();

        // A failed setup leaves USB detection dead for the whole session.
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"USB notification window creation failed (Win32 error {_windowError}).");
        if (_notifyHandle == IntPtr.Zero)
            throw new InvalidOperationException($"USB device notification registration failed (Win32 error {_notifyError}).");
    }

    /// <summary>
    /// Devices with a present USB device interface. The tracker calls this after
    /// <see cref="Start"/>, so the notification window already exists and nothing can slip
    /// between sweep and subscription; the tracker's duplicate-path guard drops a device
    /// delivered by both.
    /// </summary>
    public IEnumerable<UsbDeviceInfo> EnumeratePresent()
    {
        List<UsbDeviceInfo> devices = [];
        try
        {
            foreach (string path in GetDeviceInterfaceList(GuidDevInterfaceUsbDevice))
            {
                if (BuildDeviceInfo(path) is { } device)
                    devices.Add(device);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Initial USB device enumeration failed: {ex.Message}");
        }
        return devices;
    }

    public void Stop()
    {
        lock (_pendingLock)
        {
            foreach (CancellationTokenSource pending in _pendingArrivals.Values)
                pending.Cancel();
            _pendingArrivals.Clear();
        }
        uint tid = _messageThreadId;
        if (tid != 0)
        {
            PostThreadMessageW(tid, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _messageThread?.Join(TimeSpan.FromSeconds(2));
        _messageThread = null;
        _messageThreadId = 0;
    }

    public void Dispose() => Stop();

    private void MessagePump()
    {
        _messageThreadId = Interop.Kernel32.GetCurrentThreadId();

        // Unique class name avoids conflicts if the process hosts multiple instances.
        string className = $"QmkUsbDetector_{Environment.ProcessId}";
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate!),
            hInstance = Interop.Kernel32.GetModuleHandleW(null),
            lpszClassName = className,
        };
        RegisterClassExW(ref wndClass);

        _hwnd = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, Interop.Kernel32.GetModuleHandleW(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            _windowError = Marshal.GetLastPInvokeError();
        }
        else
        {
            var filter = new DEV_BROADCAST_DEVICEINTERFACE
            {
                Size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
                DeviceType = DBT_DEVTYP_DEVICEINTERFACE,
                ClassGuid = GuidDevInterfaceUsbDevice,
            };
            _notifyHandle = RegisterDeviceNotificationW(_hwnd, ref filter, DEVICE_NOTIFY_WINDOW_HANDLE);
            if (_notifyHandle == IntPtr.Zero)
                _notifyError = Marshal.GetLastPInvokeError();
        }

        _hwndReady.Set();

        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0))
        {
            DispatchMessageW(ref msg);
        }

        if (_notifyHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notifyHandle);
            _notifyHandle = IntPtr.Zero;
        }
        _hwnd = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_DEVICECHANGE && lParam != IntPtr.Zero)
        {
            int eventType = wParam.ToInt32();
            if (eventType is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
            {
                DEV_BROADCAST_DEVICEINTERFACE hdr = Marshal.PtrToStructure<DEV_BROADCAST_DEVICEINTERFACE>(lParam);
                if (hdr.DeviceType == DBT_DEVTYP_DEVICEINTERFACE)
                {
                    int nameOffset = Marshal.OffsetOf<DEV_BROADCAST_DEVICEINTERFACE>(
                        nameof(DEV_BROADCAST_DEVICEINTERFACE.Name)).ToInt32();
                    string deviceInterfacePath = Marshal.PtrToStringUni(lParam + nameOffset) ?? "";

                    if (eventType == DBT_DEVICEARRIVAL)
                    {
                        ScheduleArrival(deviceInterfacePath);
                    }
                    else
                    {
                        // The lock orders this against a concurrent settle completion: a device
                        // is either announced before its removal or never announced at all.
                        lock (_pendingLock)
                        {
                            if (_pendingArrivals.Remove(deviceInterfacePath, out CancellationTokenSource? pending))
                                pending.Cancel();
                            else
                                Removed?.Invoke(new UsbRemovalHint(deviceInterfacePath));
                        }
                    }
                }
            }
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ScheduleArrival(string deviceInterfacePath)
    {
        var cts = new CancellationTokenSource();
        lock (_pendingLock)
        {
            // A re-arrival on the same path replaces any stale pending settle.
            if (_pendingArrivals.TryGetValue(deviceInterfacePath, out CancellationTokenSource? stale))
                stale.Cancel();
            _pendingArrivals[deviceInterfacePath] = cts;
        }
        _ = Task.Run(() => SettleAndRaiseAsync(deviceInterfacePath, cts));
    }

    /// <summary>
    /// Announces a live arrival once its devnode settles. WM_DEVICECHANGE fires when the devnode
    /// is configured, before the driver stack binds: Service is still empty and a usbccgp root
    /// has no children yet, so an immediate read misreports the drivers and the mass-storage
    /// flag. Driverless nodes settle fast via their problem code; the timeout covers the rest.
    /// </summary>
    private async Task SettleAndRaiseAsync(string deviceInterfacePath, CancellationTokenSource cts)
    {
        try
        {
            for (int elapsed = 0; elapsed < SettleTimeoutMs && !IsDevNodeSettled(deviceInterfacePath); elapsed += SettlePollMs)
                await Task.Delay(SettlePollMs, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_pendingLock)
        {
            // Only the still-registered settle may announce; a removal or re-arrival won the race otherwise.
            if (!_pendingArrivals.TryGetValue(deviceInterfacePath, out CancellationTokenSource? current) || current != cts)
                return;
            _pendingArrivals.Remove(deviceInterfacePath);
            if (BuildDeviceInfo(deviceInterfacePath) is { } device)
                Arrived?.Invoke(device);
        }
    }

    private static bool IsDevNodeSettled(string deviceInterfacePath)
    {
        string instanceId = UsbDeviceParser.InterfacePathToInstanceId(deviceInterfacePath);
        if (CM_Locate_DevNodeW(out uint devNode, instanceId, 0) != CR_SUCCESS)
            return true;
        string service = GetDevNodeProperty(devNode, CM_DRP_SERVICE);
        if (service.Length == 0)
        {
            // No service and no problem code means driver binding is still in progress.
            return CM_Get_DevNode_Status(out uint status, out _, devNode, 0) == CR_SUCCESS
                && (status & DN_HAS_PROBLEM) != 0;
        }
        // A usbccgp root settles when its child functions have enumerated.
        return !string.Equals(service, "usbccgp", StringComparison.OrdinalIgnoreCase)
            || CollectChildServices(devNode).Count > 0;
    }

    private static UsbDeviceInfo? BuildDeviceInfo(string deviceInterfacePath)
    {
        string instanceId = UsbDeviceParser.InterfacePathToInstanceId(deviceInterfacePath);

        // Composite child functions (&MI_xx) are not devices; their root node carries the whole
        // board. The present-device list can include them (e.g. an RP2040 BOOTSEL's picotool and
        // USBSTOR functions), but WM_DEVICECHANGE only ever delivers roots, so a swept child
        // would be a phantom entry whose removal never arrives.
        if (instanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!UsbDeviceParser.TryParseHwId(instanceId, out ushort vid, out ushort pid, out ushort rev))
            return null;

        string product = "";
        string manufacturer = "";
        bool isMassStorage = false;
        List<string> services = [];

        if (CM_Locate_DevNodeW(out uint devNode, instanceId, 0) == CR_SUCCESS)
        {
            // Instance IDs never carry REV_; the hardware-ID list (REG_MULTI_SZ) does,
            // e.g. USB\VID_03EB&PID_2FF4&REV_0936.
            if (rev == 0 &&
                UsbDeviceParser.TryParseRevisionFromHardwareIds(GetDevNodeMultiSz(devNode, CM_DRP_HARDWAREID), out ushort hwRev))
            {
                rev = hwRev;
            }
            product = GetDevNodeProperty(devNode, CM_DRP_DEVICEDESC);
            manufacturer = GetDevNodeProperty(devNode, CM_DRP_MFG);
            string service = GetDevNodeProperty(devNode, CM_DRP_SERVICE);
            services = service.Length > 0 ? [service] : [];
            // usbccgp is the USB composite device driver, bound to the root rather than to any
            // function, so the bound drivers are the ones on its children.
            if (string.Equals(service, "usbccgp", StringComparison.OrdinalIgnoreCase))
                services = CollectChildServices(devNode);
            isMassStorage = services.Any(IsMassStorageService);
        }

        return new UsbDeviceInfo(vid, pid, rev, manufacturer, product, deviceInterfacePath, isMassStorage, services);
    }

    private static bool IsMassStorageService(string service) =>
        string.Equals(service, "USBSTOR", StringComparison.OrdinalIgnoreCase);

    private static List<string> CollectChildServices(uint rootDevNode)
    {
        var services = new List<string>();
        try
        {
            if (CM_Get_Child(out uint child, rootDevNode, 0) != CR_SUCCESS)
            {
                return services;
            }
            do
            {
                string svc = GetDevNodeProperty(child, CM_DRP_SERVICE);
                if (!string.IsNullOrEmpty(svc) &&
                    !string.Equals(svc, "usbccgp", StringComparison.OrdinalIgnoreCase))
                {
                    services.Add(svc);
                }
            }
            while (CM_Get_Sibling(out child, child, 0) == CR_SUCCESS);
        }
        catch (Exception ex) { Trace.WriteLine($"cfgmgr32 composite interface query failed: {ex.Message}"); }
        return services;
    }
}
