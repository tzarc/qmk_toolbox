using System.Runtime.InteropServices;
using System.Text;

namespace QmkToolbox.Usb.Discovery.Linux;

/// <summary>
/// Linux probe: hotplug via a raw netlink kobject-uevent socket (kernel group: no udevd, no
/// libudev, no native shim library), sysfs enrichment, and a /sys/bus/usb sweep of
/// already-present devices. Kernel uevents deliver exactly one add and one remove per USB
/// device (<c>DEVTYPE=usb_device</c>), both carrying VID/PID/bcdDevice in <c>PRODUCT=</c>.
/// </summary>
internal sealed class LinuxUsbProbe : IUsbProbe
{
    private const string SysfsRoot = "/sys";
    private const string SysfsDevicesRoot = "/sys/bus/usb/devices";

    private const int AF_NETLINK = 16;
    private const int SOCK_RAW = 3;
    private const int SOCK_CLOEXEC = 0x80000; // consumers spawn child processes; do not leak the fd
    private const int NETLINK_KOBJECT_UEVENT = 15;
    private const uint KERNEL_EVENT_GROUP = 1;  // group 2 is udevd's processed stream
    private const short POLLIN = 0x001;
    private const int EINTR = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SockaddrNl
    {
        public ushort Family;
        public ushort Pad;
        public uint Pid;    // 0: the kernel assigns a unique port id
        public uint Groups;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    private static extern int bind(int fd, ref SockaddrNl addr, uint addrlen);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(ref PollFd fds, uint nfds, int timeoutMs);

    [DllImport("libc", SetLastError = true)]
    private static extern nint recv(int fd, byte[] buffer, nuint length, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    public event Action<UsbDeviceInfo>? Arrived;
    public event Action<UsbRemovalHint>? Removed;

    public StringComparison PathComparison => StringComparison.Ordinal;

    private Thread? _listenThread;
    private volatile bool _stopping;
    private int _fd = -1;

    public void Start()
    {
        _fd = socket(AF_NETLINK, SOCK_RAW | SOCK_CLOEXEC, NETLINK_KOBJECT_UEVENT);
        if (_fd < 0)
            throw new InvalidOperationException($"netlink uevent socket creation failed (errno {Marshal.GetLastPInvokeError()}).");

        var addr = new SockaddrNl { Family = AF_NETLINK, Groups = KERNEL_EVENT_GROUP };
        if (bind(_fd, ref addr, (uint)Marshal.SizeOf<SockaddrNl>()) != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            _ = close(_fd);
            _fd = -1;
            throw new InvalidOperationException($"netlink uevent socket bind failed (errno {errno}).");
        }

        _stopping = false;
        _listenThread = new Thread(Listen) { IsBackground = true, Name = "UsbNetlinkListener" };
        _listenThread.Start();
    }

    public void Stop()
    {
        _stopping = true;
        if (_fd >= 0)
        {
            _ = close(_fd);
            _fd = -1;
        }
        _listenThread?.Join(TimeSpan.FromSeconds(2));
        _listenThread = null;
    }

    public void Dispose() => Stop();

    private void Listen()
    {
        // uevent datagrams are well under a page; oversize to be safe.
        byte[] buffer = new byte[8192];
        while (!_stopping)
        {
            var pfd = new PollFd { Fd = _fd, Events = POLLIN };
            int ready = poll(ref pfd, 1, 500);
            if (ready < 0)
            {
                if (Marshal.GetLastPInvokeError() == EINTR)
                    continue;
                return;
            }
            if (ready == 0)
                continue;

            nint length = recv(_fd, buffer, (nuint)buffer.Length, 0);
            if (length <= 0)
                return;

            if (ParseUevent(buffer.AsSpan(0, (int)length)) is not { } uevent)
                continue;
            if (uevent.IsAdd)
            {
                if (BuildArrival(uevent, SysfsRoot) is { } device)
                    Arrived?.Invoke(device);
            }
            else
            {
                Removed?.Invoke(new UsbRemovalHint(SysfsRoot + uevent.DevPath, uevent.Vid, uevent.Pid));
            }
        }
    }

    internal readonly record struct UsbUevent(bool IsAdd, string DevPath, ushort Vid, ushort Pid, ushort Rev);

    /// <summary>
    /// Parses a kernel uevent datagram (null-separated KEY=VALUE pairs after an
    /// "action@devpath" header) into a USB device add/remove event, or null for anything else
    /// (other subsystems, interface events, other actions, udevd's "libudev"-tagged stream).
    /// <c>PRODUCT=</c> is "vid/pid/bcdDevice" in unpadded hex, e.g. "2e8a/3/100".
    /// </summary>
    internal static UsbUevent? ParseUevent(ReadOnlySpan<byte> datagram)
    {
        if (datagram.StartsWith("libudev\0"u8))
            return null;

        string? action = null, devPath = null, subsystem = null, devType = null, product = null;
        while (!datagram.IsEmpty)
        {
            int nul = datagram.IndexOf((byte)0);
            ReadOnlySpan<byte> segment = nul < 0 ? datagram : datagram[..nul];
            datagram = nul < 0 ? default : datagram[(nul + 1)..];
            if (segment.IsEmpty)
                continue;

            string pair = Encoding.UTF8.GetString(segment);
            int eq = pair.IndexOf('=');
            if (eq <= 0)
                continue; // the "action@devpath" header segment
            string value = pair[(eq + 1)..];
            switch (pair[..eq])
            {
                case "ACTION":
                    action = value;
                    break;
                case "DEVPATH":
                    devPath = value;
                    break;
                case "SUBSYSTEM":
                    subsystem = value;
                    break;
                case "DEVTYPE":
                    devType = value;
                    break;
                case "PRODUCT":
                    product = value;
                    break;
            }
        }

        if (subsystem != "usb" || devType != "usb_device" || devPath == null)
            return null;
        if (action is not ("add" or "remove"))
            return null;

        ushort vid = 0, pid = 0, rev = 0;
        if (product?.Split('/') is { Length: >= 3 } parts)
        {
            _ = ushort.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out vid);
            _ = ushort.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out pid);
            _ = ushort.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out rev);
        }
        return new UsbUevent(action == "add", devPath, vid, pid, rev);
    }

    /// <summary>
    /// Builds the arrival payload for an add event: identity and revision come from the uevent
    /// itself; strings and the mass-storage flag from sysfs. Hubs and devices without a usable
    /// identity yield null.
    /// </summary>
    internal static UsbDeviceInfo? BuildArrival(UsbUevent uevent, string sysfsRoot)
    {
        if (uevent.Vid == 0 && uevent.Pid == 0)
            return null;
        string syspath = sysfsRoot + uevent.DevPath;
        if (LinuxUsbSysfs.ReadAttribute(syspath, "bDeviceClass") == "09")
            return null; // hub
        return new UsbDeviceInfo(
            uevent.Vid, uevent.Pid, uevent.Rev,
            LinuxUsbSysfs.ReadAttribute(syspath, "manufacturer") ?? "",
            LinuxUsbSysfs.ReadAttribute(syspath, "product") ?? "",
            "",
            syspath,
            LinuxUsbSysfs.HasMassStorageInterface(syspath));
    }

    public IEnumerable<UsbDeviceInfo> EnumeratePresent() => EnumeratePresent(SysfsDevicesRoot);

    /// <summary>
    /// Walks a sysfs USB device directory: entries carrying idVendor/idProduct are device nodes
    /// (interface and endpoint entries have neither); hubs (bDeviceClass 09) are skipped, like
    /// the Windows sweep's device-interface filter. Symlinked entries resolve to the canonical
    /// /sys/devices/… syspath so swept devices dedup against later uevents for the same device.
    /// </summary>
    internal static IReadOnlyList<UsbDeviceInfo> EnumeratePresent(string sysfsRoot)
    {
        List<UsbDeviceInfo> devices = [];
        try
        {
            if (!Directory.Exists(sysfsRoot))
                return devices;
            foreach (string entry in Directory.EnumerateDirectories(sysfsRoot))
            {
                if (!UsbDeviceParser.TryParseUsbId(LinuxUsbSysfs.ReadAttribute(entry, "idVendor"), out ushort vid) ||
                    !UsbDeviceParser.TryParseUsbId(LinuxUsbSysfs.ReadAttribute(entry, "idProduct"), out ushort pid))
                {
                    continue;
                }

                if (LinuxUsbSysfs.ReadAttribute(entry, "bDeviceClass") == "09")
                    continue;

                string syspath = LinuxUsbSysfs.ResolveRealPath(entry) ?? entry;
                devices.Add(new UsbDeviceInfo(
                    vid, pid,
                    LinuxUsbSysfs.ReadBcdDevice(syspath),
                    LinuxUsbSysfs.ReadAttribute(entry, "manufacturer") ?? "",
                    LinuxUsbSysfs.ReadAttribute(entry, "product") ?? "",
                    "",
                    syspath,
                    LinuxUsbSysfs.HasMassStorageInterface(syspath)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed sweep must never break startup; hotplug events still work.
        }
        return devices;
    }

}
