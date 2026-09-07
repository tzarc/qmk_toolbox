using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QmkToolbox.Usb.Discovery.Windows;

// WMI (Win32_PnPEntity event queries) is unsuitable here: "WITHIN n" polling delays events,
// WMI initialisation adds ~7 seconds of cold-start latency in a fresh .NET 10 process, and its
// reflection/COM plumbing breaks under PublishTrimmed. RegisterDeviceNotification avoids all
// three: the driver stack delivers arrival and removal events straight to the window procedure.

/// <summary>
/// Windows probe using RegisterDeviceNotification via a message-only window. Removal hints carry
/// the interface path only: Windows interface paths are canonical and always present, so the
/// tracker never needs a VID/PID fallback. The probe announces live arrivals only after the
/// devnode settles, so the driver name and mass-storage flag reflect the bound driver stack.
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
    // The delegate must outlive the unmanaged window class registration, so it lives in a field.
    private WndProcDelegate? _wndProcDelegate;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // GUID_DEVINTERFACE_USB_DEVICE: fires only for root USB device nodes, not composite children.
    private static readonly Guid GuidDevInterfaceUsbDevice =
        new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    private const uint WM_QUIT = 0x0012;
    private const uint WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 5;    // DEV_BROADCAST_DEVICEINTERFACE
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    private const int CR_SUCCESS = 0x00000000;
    private const uint CM_DRP_DEVICEDESC = 0x00000001; // SPDRP_DEVICEDESC: device description / product string (REG_SZ)
    private const uint CM_DRP_HARDWAREID = 0x00000002; // SPDRP_HARDWAREID: hardware IDs incl. REV_ (REG_MULTI_SZ)
    private const uint CM_DRP_SERVICE = 0x00000005;    // SPDRP_SERVICE: driver service name e.g. "WinUSB" (REG_SZ)
    private const uint CM_DRP_MFG = 0x0000000C;        // SPDRP_MFG: manufacturer string (REG_SZ)

    private const uint DN_HAS_PROBLEM = 0x00000400;

    private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;

    // Driver service names in priority order for composite device interface selection.
    private static readonly string[] DriverPriority =
        ["WinUSB", "libusbK", "libusb0", "HidUsb", "usbser", "USBSTOR"];

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public char Name; // first char of variable-length null-terminated path string
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX, ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotificationW(
        IntPtr hRecipient, ref DEV_BROADCAST_DEVICEINTERFACE notificationFilter, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Child(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Sibling(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(
        uint dnDevInst, uint ulProperty, out uint pulRegDataType,
        char[] buffer, ref uint pulLength, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_List_SizeW(
        out uint pulLen, ref Guid interfaceClassGuid, string? pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid, string? pDeviceID, char[] buffer, uint bufferLen, uint ulFlags);

    public void Start()
    {
        _wndProcDelegate = WndProc;
        _hwndReady.Reset();
        _messageThread = new Thread(MessagePump) { IsBackground = true, Name = "UsbDetectorMessagePump" };
        _messageThread.Start();
        _hwndReady.Wait();

        // A failed setup kills USB detection for the whole session; throw so the caller
        // can surface it as a visible error.
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
            Guid guid = GuidDevInterfaceUsbDevice;
            if (CM_Get_Device_Interface_List_SizeW(out uint len, ref guid, null, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS || len <= 1)
                return devices;
            char[] buffer = new char[len];
            if (CM_Get_Device_Interface_ListW(ref guid, null, buffer, len, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
                return devices;
            foreach (string path in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
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
        _messageThreadId = GetCurrentThreadId();

        // Unique class name avoids conflicts if the process hosts multiple instances.
        string className = $"QmkUsbDetector_{Environment.ProcessId}";
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate!),
            hInstance = GetModuleHandleW(null),
            lpszClassName = className,
        };
        RegisterClassExW(ref wndClass);

        // HWND_MESSAGE (-3): message-only window with no taskbar entry and no screen presence.
        _hwnd = CreateWindowExW(0, className, null, 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

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
    /// has no children yet, so an immediate read misreports the driver and the mass-storage
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
        string service = ReadDevNodeProperty(devNode, CM_DRP_SERVICE);
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
        string driver = "";
        bool isMassStorage = false;

        if (CM_Locate_DevNodeW(out uint devNode, instanceId, 0) == CR_SUCCESS)
        {
            // Instance IDs never carry REV_; the hardware-ID list (REG_MULTI_SZ) does,
            // e.g. USB\VID_03EB&PID_2FF4&REV_0936.
            if (rev == 0 &&
                UsbDeviceParser.TryParseRevisionFromHardwareIds(ReadDevNodeMultiSz(devNode, CM_DRP_HARDWAREID), out ushort hwRev))
            {
                rev = hwRev;
            }
            product = ReadDevNodeProperty(devNode, CM_DRP_DEVICEDESC);
            manufacturer = ReadDevNodeProperty(devNode, CM_DRP_MFG);
            string service = ReadDevNodeProperty(devNode, CM_DRP_SERVICE);
            isMassStorage = IsMassStorageService(service);
            // usbccgp is the USB composite device driver; surface the most relevant child interface instead.
            if (string.Equals(service, "usbccgp", StringComparison.OrdinalIgnoreCase))
            {
                List<string> services = CollectChildServices(devNode);
                // The priority pick below can mask USBSTOR behind e.g. HidUsb, so the
                // mass-storage flag looks at every child function, not just the winner.
                isMassStorage = services.Any(IsMassStorageService);
                service = PickBestInterfaceService(services);
            }
            driver = service;
        }

        return new UsbDeviceInfo(vid, pid, rev, manufacturer, product, driver, deviceInterfacePath, isMassStorage);
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
                string svc = ReadDevNodeProperty(child, CM_DRP_SERVICE);
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

    private static string PickBestInterfaceService(List<string> services)
    {
        foreach (string preferred in DriverPriority)
        {
            if (services.Any(s => string.Equals(s, preferred, StringComparison.OrdinalIgnoreCase)))
            {
                return preferred;
            }
        }
        return services.FirstOrDefault() ?? "";
    }

    private static string ReadDevNodeProperty(uint devNode, uint property)
    {
        char[] buffer = new char[260];
        uint size = (uint)(buffer.Length * 2);
        return CM_Get_DevNode_Registry_PropertyW(devNode, property, out _, buffer, ref size, 0) == CR_SUCCESS && size > 2
            ? new string(buffer, 0, (int)(size / 2) - 1)
            : "";
    }

    private static string[] ReadDevNodeMultiSz(uint devNode, uint property)
    {
        char[] buffer = new char[1024];
        uint size = (uint)(buffer.Length * 2);
        return CM_Get_DevNode_Registry_PropertyW(devNode, property, out _, buffer, ref size, 0) != CR_SUCCESS || size < 2
            ? []
            : new string(buffer, 0, (int)(size / 2)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
