namespace QmkToolbox.Usb.Hid;

/// <summary>
/// An open HID interface: raises input reports as they arrive and writes output reports.
/// Subscribe, then call <see cref="Start"/>. For interfaces that use no report IDs, payloads
/// carry no report-ID byte in either direction, so the same bytes move on every platform.
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
    /// interface is gone or the device is disposed.
    /// </summary>
    public abstract bool Write(ReadOnlySpan<byte> payload);

    /// <summary>Stops report delivery and releases the interface; raises <see cref="Closed"/> if the interface was still up.</summary>
    public abstract void Dispose();

    /// <summary>Raises <see cref="ReportReceived"/>. Subscribers keep <paramref name="payload"/>, so pass an array the implementation will not reuse.</summary>
    protected void RaiseReport(byte[] payload) => ReportReceived?.Invoke(payload);

    /// <summary>Raises <see cref="Closed"/>. Call this exactly once.</summary>
    protected void RaiseClosed() => Closed?.Invoke();
}
