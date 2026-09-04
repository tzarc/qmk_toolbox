using QmkToolbox.Desktop.Models;

namespace QmkToolbox.Tests;

internal static class TerminalText
{
    /// <summary>
    /// The buffer's raw text: segment concat, '\n' between lines, no prefixes. Prefixes and
    /// rendering belong to TerminalProjection; buffer-level tests assert content only.
    /// </summary>
    public static string Flatten(TerminalBuffer buffer) =>
        string.Join('\n',
            (buffer.CurrentLine.Segments.Count > 0 ? buffer.Lines.Append(buffer.CurrentLine) : buffer.Lines)
            .Select(l => string.Concat(l.Segments.Select(seg => seg.Text))));
}
