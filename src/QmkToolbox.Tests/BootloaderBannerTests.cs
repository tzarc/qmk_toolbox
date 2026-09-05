using QmkToolbox.Core.Bootloader;
using QmkToolbox.Core.Models;
using Xunit;

namespace QmkToolbox.Tests;

public class BootloaderBannerTests
{
    /// <summary>
    /// Regression guard: a new <see cref="BootloaderType"/> must be added to a banner row, or
    /// excluded here, before it ships; otherwise the startup banner drifts from the
    /// supported-bootloader set.
    /// </summary>
    [Fact]
    public void EveryBootloaderTypeAppearsInExactlyOneBannerRow()
    {
        List<BootloaderType> listed =
            [.. BootloaderBanner.Bootloaders.Concat(BootloaderBanner.IspFlashers).SelectMany(e => e.Types)];

        foreach (BootloaderType type in Enum.GetValues<BootloaderType>())
        {
            if (type == BootloaderType.None)
                continue;
            Assert.Equal(1, listed.Count(t => t == type));
        }

        Assert.Equal(Enum.GetValues<BootloaderType>().Length - 1, listed.Count);
    }
}
