using Xunit;

namespace Qmk.Usb.Discovery.Tests;

// xUnit v2 has no built-in conditional skip; a custom FactAttribute subclass
// sets Skip when the condition is false, making skipped tests visible in the runner
// rather than silently passing.
public class FactOnLinuxAttribute : FactAttribute
{
    public FactOnLinuxAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test";
    }
}
