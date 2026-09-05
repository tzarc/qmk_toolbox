namespace QmkToolbox.Desktop.Models;

/// <summary>
/// The firmware file extensions the app accepts. The file picker and drag-and-drop
/// both filter on this list.
/// </summary>
public static class FirmwareFiles
{
    public static readonly string[] Extensions = [".hex", ".bin", ".uf2"];

    /// <summary>File-picker glob patterns, e.g. <c>*.hex</c>.</summary>
    public static readonly string[] PickerPatterns = [.. Extensions.Select(e => "*" + e)];

    public static bool IsFirmwareFile(string path) =>
        Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
