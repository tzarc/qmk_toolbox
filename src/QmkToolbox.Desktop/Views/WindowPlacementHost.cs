using Avalonia;
using Avalonia.Controls;
using QmkToolbox.Desktop.Models;
using QmkToolbox.Desktop.Services;

namespace QmkToolbox.Desktop.Views;

/// <summary>
/// Restores a window's last position and size when it opens and records them when it closes.
/// Placement policy lives in <see cref="WindowPlacement"/>; this is the Avalonia wiring.
/// </summary>
internal static class WindowPlacementHost
{
    /// <summary>
    /// Tracks <paramref name="window"/> under its type name. Each window writes settings as it
    /// closes, because auxiliary windows close after the main window has already saved.
    /// </summary>
    public static void Track(Window window, SettingsService settings)
    {
        string key = window.GetType().Name;

        window.Opened += (_, _) =>
        {
            WindowBounds? saved = settings.Current.WindowBounds.TryGetValue(key, out WindowBounds b) ? b : null;
            (Size? size, PixelPoint? position) = WindowPlacement.Restore(saved, window.Screens.All.Select(s => s.WorkingArea));
            if (size is { } s)
            {
                window.Width = s.Width;
                window.Height = s.Height;
            }
            if (position is { } p)
                window.Position = p;
        };

        window.Closing += (_, e) =>
        {
            if (e.Cancel)
                return;
            settings.Current.WindowBounds[key] =
                new WindowBounds(window.Position.X, window.Position.Y, window.Width, window.Height);
            settings.Save();
        };
    }
}
