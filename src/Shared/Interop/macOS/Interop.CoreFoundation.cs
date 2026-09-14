using System.Runtime.InteropServices;
using System.Runtime.Versioning;

internal static partial class Interop
{
    /// <summary>CoreFoundation string/number unwrapping and the run loop the IOKit notification port attaches to.</summary>
    [SupportedOSPlatform("macos")]
    internal static class CoreFoundation
    {
        internal const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        internal const uint KCfStringEncodingUtf8 = 0x08000100;
        internal const int KCfNumberIntType = 9;
        internal const string KCfRunLoopDefaultMode = "kCFRunLoopDefaultMode";

        [DllImport(CoreFoundationLib, ExactSpelling = true)]
        internal static extern void CFRelease(IntPtr cf);

        [DllImport(CoreFoundationLib, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

        [DllImport(CoreFoundationLib, ExactSpelling = true)]
        internal static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, long bufferSize, uint encoding);

        [DllImport(CoreFoundationLib, ExactSpelling = true)]
        internal static extern bool CFNumberGetValue(IntPtr number, int theType, out int value);

        [DllImport(CoreFoundationLib, ExactSpelling = true)]
        internal static extern IntPtr CFRunLoopGetCurrent();

        [DllImport(CoreFoundationLib, ExactSpelling = true)]
        internal static extern void CFRunLoopAddSource(IntPtr runLoop, IntPtr source, IntPtr mode);

        [DllImport(CoreFoundationLib, ExactSpelling = true)]
        internal static extern void CFRunLoopRun();

        [DllImport(CoreFoundationLib, ExactSpelling = true)]
        internal static extern void CFRunLoopStop(IntPtr runLoop);
    }
}
