namespace QmkToolbox.Usb.Hid;

/// <summary>
/// An open HID interface: raises input reports as they arrive and writes output reports.
/// Subscribe, then call <see cref="Start"/>. Payloads carry no report-ID byte in either
/// direction for interfaces that use none, so the same bytes move on every platform.
/// </summary>
public abstract class HidInterfaceDevice : IDisposable
{
    /// <summary>
    /// Raised with each input report's payload, on the device's read thread; marshal in
    /// your handler. The array is the subscriber's to keep.
    /// </summary>
    public event Action<byte[]>? ReportReceived;

    /// <summary>Raised once when the interface is gone or disposed; no reports follow it.</summary>
    public event Action? Closed;

    /// <summary>Starts delivering reports. Subscribe before calling.</summary>
    public abstract void Start();

    /// <summary>
    /// Writes <paramref name="payload"/> as one output report. Returns false once the
    /// interface is gone.
    /// </summary>
    public abstract bool Write(ReadOnlySpan<byte> payload);

    /// <summary>Stops report delivery and releases the interface; raises <see cref="Closed"/> if the interface was still up.</summary>
    public abstract void Dispose();

    /// <summary>Raises <see cref="ReportReceived"/>; the implementation hands ownership of the array to subscribers.</summary>
    protected void RaiseReport(byte[] payload) => ReportReceived?.Invoke(payload);

    /// <summary>Raises <see cref="Closed"/>; implementations raise it exactly once.</summary>
    protected void RaiseClosed() => Closed?.Invoke();
}
