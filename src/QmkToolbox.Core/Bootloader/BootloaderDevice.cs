using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;
using QmkToolbox.Usb.Discovery;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Base class for bootloader devices. Wraps a <see cref="UsbDeviceInfo"/> and provides the
/// plumbing shared by flashing, EEPROM operations, reset, and tool invocation.
/// </summary>
public abstract class BootloaderDevice(UsbDeviceInfo device, BootloaderServices services, bool resolvesComPort = false)
{
    public UsbDeviceInfo Device { get; } = device;
    protected BootloaderServices Services { get; } = services;

    public ushort VendorId => Device.VendorId;
    public ushort ProductId => Device.ProductId;
    public IReadOnlySet<string> Drivers => Device.Drivers;
    public string DevicePath => Device.DevicePath;

    public string PreferredDriver { get; init; } = "";
    public bool IsEepromFlashable { get; init; }
    public bool IsResettable { get; init; }
    public BootloaderType Type { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Background port resolution when constructed with <c>resolvesComPort</c>; null otherwise.</summary>
    // The first port the resolver yields is the device's primary interface, the one a
    // bootloader is flashed through.
    protected Task<string?>? ComPortTask { get; } = resolvesComPort
        ? PollAsync(() => services.UsbResolver.EnumerateSerialPorts(device).FirstOrDefault(), services.PollDelayMs)
        : null;

#pragma warning disable VSTHRD002 // guarded by IsCompletedSuccessfully: Result on a completed task cannot block
    public override string ToString() =>
        ComPortTask is { IsCompletedSuccessfully: true }
            ? $"{Device} [{ComPortTask.Result ?? "port not found"}]"
            : Device.ToString()!;
#pragma warning restore VSTHRD002

    /// <summary>Resolves when the device is ready to display (e.g. serial port has appeared).</summary>
    public virtual Task WhenReadyAsync() => ComPortTask ?? Task.CompletedTask;

    public abstract Task FlashAsync(string mcu, string file);

    public virtual Task FlashEepromAsync(string mcu, string file) => Task.CompletedTask;

    public virtual Task ResetAsync(string mcu) => Task.CompletedTask;

    /// <summary>
    /// Cancelled when this device is removed while an operation is running against it. The
    /// orchestrator sets it for that operation's duration only; at any other time the device
    /// observes <see cref="CancellationToken.None"/>.
    /// </summary>
    internal CancellationToken OperationToken { get; set; }

    protected Task<int> RunToolAsync(string toolName, params string[] args) =>
        FlashService.RunToolAsync(toolName, args, Services.ToolProvider, Services.Output,
            Services.ProcessRunner, Services.TimeProvider, OperationToken);

    protected void Output(string message, MessageType type) => Services.Output(message, type);

    /// <summary>
    /// Throws <see cref="UnsupportedFileFormatException"/> if the file's extension is not in the accepted list.
    /// </summary>
    protected static void ValidateFileExtension(string file, params string[] extensions)
    {
        if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            throw new UnsupportedFileFormatException(extensions);
    }

    // Serial ports (Caterina et al.) and mass-storage volumes appear some time after
    // the USB arrival event; polling with short delays covers that gap.
    private static async Task<string?> PollAsync(Func<string?> resolve, int delayMs)
    {
        const int attempts = 10;
        for (int i = 0; i < attempts; i++)
        {
            string? result = resolve();
            if (result != null)
                return result;
            if (i < attempts - 1)
                await Task.Delay(delayMs).ConfigureAwait(false);
        }
        return null;
    }

    protected Task<string?> FindMountPointAsync(string markerFile) =>
        PollAsync(() => Services.UsbResolver.FindMarkerVolume(Device, markerFile), Services.PollDelayMs);

    /// <summary>
    /// Returns <paramref name="comPort"/> if non-null, or throws <see cref="ComPortNotFoundException"/>.
    /// </summary>
    protected string RequireComPort(string? comPort) =>
        comPort ?? throw new ComPortNotFoundException(Name);
}
