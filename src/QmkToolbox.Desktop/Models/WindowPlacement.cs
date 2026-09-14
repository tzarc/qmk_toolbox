using Avalonia;

namespace QmkToolbox.Desktop.Models;

/// <summary>A window's placement, captured at close and restored at the next open.</summary>
public readonly record struct WindowBounds(int X, int Y, double Width, double Height);

/// <summary>Window placement policy. Callers pass the work areas in; this type never touches a window or a screen.</summary>
public static class WindowPlacement
{
    /// <summary>
    /// Returns <paramref name="saved"/> when it lies within any of the given work areas, or
    /// <see langword="null"/> when it is off-screen (e.g. a monitor was removed since the last
    /// run) and the window should keep its default placement.
    /// </summary>
    public static PixelPoint? Clamp(PixelPoint saved, IEnumerable<PixelRect> workAreas) =>
        workAreas.Any(a => a.Contains(saved)) ? saved : null;

    /// <summary>
    /// Splits saved bounds into the size and position a window should open with. The size comes
    /// back whenever bounds were saved; the position only when it still falls on a screen. A
    /// null for either means the window keeps its declared default.
    /// </summary>
    public static (Size? Size, PixelPoint? Position) Restore(WindowBounds? saved, IEnumerable<PixelRect> workAreas) =>
        saved is not { } b
            ? (null, null)
            : (new Size(b.Width, b.Height), Clamp(new PixelPoint(b.X, b.Y), workAreas));
}
