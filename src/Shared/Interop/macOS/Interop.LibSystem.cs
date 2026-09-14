using System.Runtime.InteropServices;
using System.Runtime.Versioning;

internal static partial class Interop
{
    /// <summary>macOS libc: the filesystem query that maps a mount point to its backing BSD device.</summary>
    [SupportedOSPlatform("macos")]
    internal static class LibSystem
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct StatfsBuf
        {
            public uint f_bsize;
            public int f_iosize;
            public ulong f_blocks, f_bfree, f_bavail, f_files, f_ffree;
            public ulong f_fsid;
            public uint f_owner;
            public uint f_type;
            public uint f_flags;
            public uint f_fssubtype;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] f_fstypename;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] f_mntonname;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)] public byte[] f_mntfromname;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] f_reserved;
        }

        // The 64-bit-inode variant is the plain symbol on arm64 but carries the $INODE64 suffix
        // on x86_64; both RIDs build from the same source, so pick at runtime.
        [DllImport("libSystem", EntryPoint = "statfs", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern int StatfsArm64(string path, ref StatfsBuf buf);

        [DllImport("libSystem", EntryPoint = "statfs$INODE64", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern int StatfsX64(string path, ref StatfsBuf buf);

        internal static int Statfs(string path, ref StatfsBuf buf) =>
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? StatfsArm64(path, ref buf)
                : StatfsX64(path, ref buf);
    }
}
