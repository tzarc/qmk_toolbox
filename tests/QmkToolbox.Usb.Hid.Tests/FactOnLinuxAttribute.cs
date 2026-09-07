using Xunit;

namespace QmkToolbox.Usb.Hid.Tests;

// xUnit v2 has no built-in conditional skip; this FactAttribute subclass sets Skip
// so the runner reports the test as skipped instead of silently passing it.
public class FactOnLinuxAttribute : FactAttribute
{
    public FactOnLinuxAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test";
    }
}
