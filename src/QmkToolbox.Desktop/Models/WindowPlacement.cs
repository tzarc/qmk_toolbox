using Avalonia;

namespace QmkToolbox.Desktop.Models;

/// <summary>A window's placement, captured at close and restored at the next open.</summary>
public readonly record struct WindowBounds(int X, int Y, double Width, double Height);

/// <summary>Window placement policy; takes no windowing-system dependency.</summary>
public static class WindowPlacement
{
    /// <summary>
    /// Returns <paramref name="saved"/> when it lies within any of the given work areas, or
    /// <see langword="null"/> when it is off-screen (e.g. a monitor was removed since the last
    /// run) and the window should keep its default placement.
    /// </summary>
    public static PixelPoint? Clamp(PixelPoint saved, IEnumerable<PixelRect> workAreas) =>
        workAreas.Any(a => a.Contains(saved)) ? saved : null;
}
