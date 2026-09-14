namespace QmkToolbox.Core.Models;

/// <summary>
/// Receives one log line. Called from whichever thread produced the line, so a sink that
/// touches the UI marshals for itself.
/// </summary>
/// <param name="text">The line, or a raw chunk for the tool-output and HID stream types.</param>
/// <param name="type">What produced the line; the sink routes and styles on it.</param>
public delegate void MessageSink(string text, MessageType type);
