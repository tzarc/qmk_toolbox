using System.Text.RegularExpressions;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Desktop.Models;

/// <summary>
/// Projects a <see cref="TerminalBuffer"/> into a flat list of <see cref="TerminalRun"/> for the
/// view to render. This is the only place that resolves line prefixes, splits URLs out of
/// segments, and assigns absolute text offsets; the view maps runs to inlines and nothing more.
/// </summary>
public static class TerminalProjection
{
    // An http(s) URL up to the first whitespace, closing bracket, or quote.
    private static readonly Regex UrlRegex = new(@"https?://[^\s\)\]}>""']+", RegexOptions.Compiled);

    /// <summary>
    /// Flattens the buffer's completed lines and in-progress line into runs. Each completed line
    /// ends with a <see cref="TerminalRunKind.LineBreak"/> run that occupies one offset; the current
    /// line gets no trailing break, per the buffer's cursor semantics.
    /// </summary>
    public static IReadOnlyList<TerminalRun> ToRuns(TerminalBuffer buffer)
    {
        var runs = new List<TerminalRun>();
        int pos = 0;

        foreach (TerminalLine line in buffer.Lines)
        {
            AppendLine(line, runs, ref pos);
            runs.Add(new TerminalRun("\n", LineType(line), TerminalRunKind.LineBreak, pos, null));
            pos += 1;
        }

        if (buffer.CurrentLine.Segments.Count > 0)
            AppendLine(buffer.CurrentLine, runs, ref pos);

        return runs;
    }

    /// <summary>
    /// Flattens the buffer to plain text exactly as rendered: prefixes included,
    /// <see cref="Environment.NewLine"/> between lines. Clipboard export uses this, so copied
    /// text matches the display.
    /// </summary>
    public static string ToText(TerminalBuffer buffer) =>
        string.Concat(ToRuns(buffer).Select(r =>
            r.Kind == TerminalRunKind.LineBreak ? Environment.NewLine : r.Text));

    private static MessageType LineType(TerminalLine line) =>
        line.Segments.Count > 0 ? line.Segments[0].Type : default;

    private static void AppendLine(TerminalLine line, List<TerminalRun> runs, ref int pos)
    {
        if (line.Segments.Count == 0)
            return;

        // The line's prefix is keyed off its first segment's type.
        MessageType lineType = line.Segments[0].Type;
        string prefix = MessageTypeDescriptors.For(lineType).Prefix;
        if (prefix.Length > 0)
        {
            runs.Add(new TerminalRun(prefix, lineType, TerminalRunKind.Prefix, pos, null));
            pos += prefix.Length;
        }

        foreach (TerminalSegment seg in line.Segments)
        {
            string text = seg.Text;
            int lastIndex = 0;

            foreach (Match match in UrlRegex.Matches(text))
            {
                if (match.Index > lastIndex)
                {
                    string chunk = text[lastIndex..match.Index];
                    runs.Add(new TerminalRun(chunk, seg.Type, TerminalRunKind.Text, pos, null));
                    pos += chunk.Length;
                }

                string url = match.Value;
                runs.Add(new TerminalRun(url, seg.Type, TerminalRunKind.Url, pos, url));
                pos += url.Length;

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                string remaining = text[lastIndex..];
                runs.Add(new TerminalRun(remaining, seg.Type, TerminalRunKind.Text, pos, null));
                pos += remaining.Length;
            }
        }
    }
}
