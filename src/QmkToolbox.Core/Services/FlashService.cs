using System.ComponentModel;
using QmkToolbox.Core.Models;

namespace QmkToolbox.Core.Services;

public static class FlashService
{
    private const int FlashTimeoutMinutes = 5;

    /// <summary>
    /// Launches a flash tool as a child process and returns its exit code.
    /// Forwards stdout/stderr as raw text chunks as they arrive, embedded '\r'/'\n' intact
    /// and without line assembly, so a terminal-style consumer can render progress bars and
    /// partial lines immediately.
    /// </summary>
    /// <param name="toolName">Name of the tool binary (without path or extension).</param>
    /// <param name="args">Individual command-line arguments. Each element is passed as a
    /// discrete argument, so paths with spaces or special characters need no manual
    /// quoting.</param>
    /// <param name="toolProvider">Resolves tool paths and working directory.</param>
    /// <param name="output">Optional callback for stdout/stderr chunks.</param>
    /// <param name="runner">Process launcher; defaults to the real <see cref="SystemProcessRunner"/>.
    /// A fake runner lets tests drive scripted output, start failures, and timeouts.</param>
    /// <param name="timeProvider">Clock for the timeout; defaults to <see cref="TimeProvider.System"/>.
    /// A fake provider lets tests trigger the timeout deterministically.</param>
    /// <param name="cancellationToken">Kills the tool early when signalled; the call then
    /// returns -1, as it does for any other failure.</param>
    public static async Task<int> RunToolAsync(
        string toolName,
        string[] args,
        IFlashToolProvider toolProvider,
        MessageSink? output,
        IProcessRunner? runner = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        runner ??= SystemProcessRunner.Shared;
        timeProvider ??= TimeProvider.System;

        output?.Invoke(FormatCommandLine(toolName, args), MessageType.Command);

        string toolPath = toolProvider.GetToolPath(toolName);
        string workingDir = toolProvider.GetResourceFolder();

        IRunningProcess process;
        try
        {
            process = runner.Start(toolPath, workingDir, args);
        }
        catch (Win32Exception)
        {
            output?.Invoke($"Could not start process: {toolPath}", MessageType.Error);
            return -1;
        }

        using (process)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(FlashTimeoutMinutes), timeProvider))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken))
        {
            try
            {
                Task stdoutTask = PumpAsync(process.StandardOutput,
                    chunk => output?.Invoke(chunk, MessageType.CommandOutput), linked.Token);
                Task stderrTask = PumpAsync(process.StandardError,
                    chunk => output?.Invoke(chunk, MessageType.CommandError), linked.Token);

                await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                // Abandonment, timeout, or pump failure (e.g. IOException mid-read). Whichever
                // it is, the child must not be left running or abort the caller's remaining devices.
                output?.Invoke(ex switch
                {
                    OperationCanceledException when cancellationToken.IsCancellationRequested =>
                        "Flash tool stopped: the device was removed.",
                    OperationCanceledException => $"Flash tool timed out after {FlashTimeoutMinutes} minutes.",
                    _ => $"Error reading tool output: {ex.Message}",
                }, MessageType.Error);
                try
                {
                    process.Kill();
                }
                catch { }
                return -1;
            }
        }
    }

    /// <summary>
    /// Formats a tool name and arguments for display in the log (MessageType.Command).
    /// Not used for process invocation; actual execution uses ProcessStartInfo.ArgumentList.
    /// </summary>
    private static string FormatCommandLine(string toolName, string[] args) =>
        string.Join(' ', [toolName, .. args.Select(a =>
            a.Contains(' ') || a.Length == 0 ? $"\"{a.Replace("\"", "\\\"")}\"" : a)]);

    private static async Task PumpAsync(StreamReader reader, Action<string> onChunk, CancellationToken ct)
    {
        char[] buf = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
            onChunk(new string(buf, 0, count));
    }
}
