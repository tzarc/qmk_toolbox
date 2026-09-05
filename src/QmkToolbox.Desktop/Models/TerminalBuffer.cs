using System.Text;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Desktop.Models;

public readonly record struct TerminalSegment(string Text, MessageType Type);

public class TerminalLine
{
    public List<TerminalSegment> Segments { get; } = [];
}

/// <summary>
/// A terminal-style buffer with carriage-return and newline semantics.
///
/// - Ordinary characters overwrite at the cursor column; the line extends when written past its end.
/// - '\r' resets the column to 0 without starting a new line.
/// - '\n' moves the current line to <see cref="Lines"/> and starts a fresh line at column 0.
/// </summary>
public class TerminalBuffer
{
    private readonly List<TerminalLine> _lines = [];

    /// <summary> Raised whenever the buffer's contents change (write, clear, or trim). </summary>
    public event Action? Changed;

    /// <summary> Completed lines. </summary>
    public IReadOnlyList<TerminalLine> Lines => _lines;

    /// <summary> The in-progress line the cursor writes to. </summary>
    public TerminalLine CurrentLine { get; private set; } = new();

    /// <summary> Cursor column within the current line. </summary>
    public int Col { get; private set; }

    public void Clear()
    {
        _lines.Clear();
        CurrentLine = new TerminalLine();
        Col = 0;
        Changed?.Invoke();
    }

    public void Write(string text, MessageType type)
    {
        if (text.Length == 0)
            return;

        foreach (char c in text)
        {
            switch (c)
            {
                case '\r':
                    Col = 0;
                    break;

                case '\n':
                    _lines.Add(CurrentLine);
                    CurrentLine = new TerminalLine();
                    Col = 0;
                    break;

                default:
                    InsertCharAt(c, type);
                    Col++;
                    break;
            }
        }

        Changed?.Invoke();
    }

    private void InsertCharAt(char ch, MessageType type)
    {
        List<TerminalSegment> segments = CurrentLine.Segments;
        int offset = 0;

        for (int i = 0; i < segments.Count; i++)
        {
            TerminalSegment seg = segments[i];
            if (offset + seg.Text.Length > Col)
            {
                // Overwrites keep the segment's original type: a '\r' rewrite (e.g. a
                // progress bar) reuses the colour of the text it replaces.
                var sb = new StringBuilder(seg.Text);
                sb[Col - offset] = ch;
                segments[i] = seg with { Text = sb.ToString() };
                return;
            }
            offset += seg.Text.Length;
        }

        // Appends coalesce into the trailing same-type segment: URL detection and the
        // rendered inline count need contiguous runs, not one segment per character.
        int last = segments.Count - 1;
        if (last >= 0 && segments[last].Type == type)
            segments[last] = segments[last] with { Text = segments[last].Text + ch };
        else
            segments.Add(new TerminalSegment(ch.ToString(), type));
    }

    /// <summary> Trim the oldest lines to stay within maxLines. </summary>
    public void TrimToMax(int maxLines)
    {
        int total = _lines.Count + 1; // completed + current line
        if (total <= maxLines)
            return;
        _lines.RemoveRange(0, Math.Min(total - maxLines, _lines.Count));
        Changed?.Invoke();
    }
}
