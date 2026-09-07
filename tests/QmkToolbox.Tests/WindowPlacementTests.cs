using Avalonia;
using QmkToolbox.Desktop.Models;
using Xunit;

namespace QmkToolbox.Tests;

public class WindowPlacementTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1080);
    private static readonly PixelRect Secondary = new(1920, 0, 1280, 1024);

    [Fact]
    public void PositionOnPrimaryScreen_IsKept() =>
        Assert.Equal(new PixelPoint(100, 200), WindowPlacement.Clamp(new PixelPoint(100, 200), [Primary, Secondary]));

    [Fact]
    public void PositionOnSecondaryScreen_IsKept() =>
        Assert.Equal(new PixelPoint(2500, 500), WindowPlacement.Clamp(new PixelPoint(2500, 500), [Primary, Secondary]));

    // A window saved on a monitor that has since been unplugged falls back to default placement.
    [Fact]
    public void OffScreenPosition_IsDiscarded() =>
        Assert.Null(WindowPlacement.Clamp(new PixelPoint(5000, 5000), [Primary, Secondary]));

    [Fact]
    public void NegativeCoordinates_AreKeptWhenAScreenExtendsThere() =>
        Assert.Equal(new PixelPoint(-500, 100), WindowPlacement.Clamp(new PixelPoint(-500, 100), [new PixelRect(-1920, 0, 1920, 1080)]));

    [Fact]
    public void NoScreens_DiscardsThePosition() =>
        Assert.Null(WindowPlacement.Clamp(new PixelPoint(10, 10), []));
}
