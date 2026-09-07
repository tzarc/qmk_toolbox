using Xunit;

namespace QmkToolbox.Usb.Discovery.Tests;

// xUnit v2 has no built-in conditional skip. Setting Skip in the constructor shows
// the test as skipped in the runner instead of letting it pass silently.
public class FactOnLinuxAttribute : FactAttribute
{
    public FactOnLinuxAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test";
    }
}
