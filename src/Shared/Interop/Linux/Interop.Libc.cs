using System.Runtime.InteropServices;

internal static partial class Interop
{
    /// <summary>
    /// Linux libc: the raw netlink socket the kobject-uevent hotplug stream arrives on.
    /// </summary>
    /// <remarks>
    /// Deliberately not marked <c>[SupportedOSPlatform("linux")]</c>. The socket calls are POSIX
    /// and exist on every Unix; only the netlink constants are Linux-specific, and those are
    /// plain integers CA1416 has nothing to say about. Annotating this class flags every caller
    /// of the pure uevent-parsing helpers, including the tests.
    /// </remarks>
    internal static class Libc
    {
        internal const int AF_NETLINK = 16;
        internal const int SOCK_RAW = 3;
        internal const int SOCK_CLOEXEC = 0x80000; // consumers spawn child processes; do not leak the fd
        internal const int NETLINK_KOBJECT_UEVENT = 15;
        internal const uint KERNEL_EVENT_GROUP = 1;  // group 2 is udevd's processed stream
        internal const short POLLIN = 0x001;
        internal const int EINTR = 4;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SockaddrNl
        {
            public ushort Family;
            public ushort Pad;
            public uint Pid;    // 0: the kernel assigns a unique port id
            public uint Groups;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PollFd
        {
            public int Fd;
            public short Events;
            public short Revents;
        }

        [DllImport("libc", SetLastError = true)]
        internal static extern int socket(int domain, int type, int protocol);

        [DllImport("libc", SetLastError = true)]
        internal static extern int bind(int fd, ref SockaddrNl addr, uint addrlen);

        [DllImport("libc", SetLastError = true)]
        internal static extern int poll(ref PollFd fds, uint nfds, int timeoutMs);

        [DllImport("libc", SetLastError = true)]
        internal static extern nint recv(int fd, byte[] buffer, nuint length, int flags);

        [DllImport("libc", SetLastError = true)]
        internal static extern int close(int fd);
    }
}
