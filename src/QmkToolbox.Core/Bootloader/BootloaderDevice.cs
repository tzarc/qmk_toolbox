using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Abstract base class for all bootloader device implementations.
/// Wraps an <see cref="IUsbDevice"/> and provides common plumbing for flashing,
/// EEPROM operations, reset, and tool invocation.
/// </summary>
public abstract class BootloaderDevice(IUsbDevice device, IFlashToolProvider toolProvider, ISerialPortService? serialPortService = null, IMountPointService? mountPointService = null)
{
    public event Action<BootloaderDevice, string, MessageType>? OutputReceived;

    public IUsbDevice Device { get; } = device;
    protected IFlashToolProvider ToolProvider { get; } = toolProvider;
    protected ISerialPortService? SerialPortService { get; } = serialPortService;
    protected IMountPointService? MountPointService { get; } = mountPointService;

    public ushort VendorId => Device.VendorId;
    public ushort ProductId => Device.ProductId;
    public string Driver => Device.Driver;
    public string DevicePath => Device.DevicePath;

    public string PreferredDriver { get; init; } = "";
    public bool IsEepromFlashable { get; init; }
    public bool IsResettable { get; init; }
    public BootloaderType Type { get; init; }
    public string Name { get; init; } = "";

    /// <summary>
    /// Background serial-port resolution for devices that expose one; doubles as the
    /// readiness signal (<see cref="WhenReadyAsync"/>) and the <c>[port]</c> display suffix.
    /// </summary>
    protected Task<string?>? ComPortTask { get; set; }

    public override string ToString() =>
        ComPortTask is { IsCompletedSuccessfully: true }
            ? $"{Device} [{ComPortTask.Result ?? "port not found"}]"
            : Device.ToString()!;

    /// <summary>Resolves when the device is ready to display (e.g. serial port has appeared).</summary>
    public virtual Task WhenReadyAsync() => ComPortTask ?? Task.CompletedTask;

    public abstract Task FlashAsync(string mcu, string file);

    public virtual Task FlashEepromAsync(string mcu, string file) => Task.CompletedTask;

    public virtual Task ResetAsync(string mcu) => Task.CompletedTask;

    protected Task<int> RunToolAsync(string toolName, params string[] args) =>
        FlashService.RunToolAsync(toolName, args, ToolProvider, PrintMessage);

    protected void PrintMessage(string message, MessageType type) =>
        OutputReceived?.Invoke(this, message, type);

    /// <summary>
    /// Throws <see cref="UnsupportedFileFormatException"/> if the file's extension is not in the accepted list.
    /// </summary>
    protected static void ValidateFileExtension(string file, params string[] extensions)
    {
        if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            throw new UnsupportedFileFormatException(extensions);
    }

    // Poll cadence for serial-port/mount resolution: a test seam like
    // FlashOrchestrator.VolumeProbeDelayMs; production always uses the default.
    public int PollDelayMs { get; set; } = 250;

    // Serial ports (Caterina et al.) and mass-storage volumes both appear some time after
    // the USB arrival event; poll with short delays so the resource has time to appear.
    private async Task<string?> PollAsync(Func<string?> resolve)
    {
        const int attempts = 10;
        for (int i = 0; i < attempts; i++)
        {
            string? result = resolve();
            if (result != null)
                return result;
            if (i < attempts - 1)
                await Task.Delay(PollDelayMs).ConfigureAwait(false);
        }
        return null;
    }

    protected Task<string?> FindComPortAsync() =>
        SerialPortService == null ? Task.FromResult<string?>(null) : PollAsync(() => SerialPortService.FindSerialPort(Device));

    protected Task<string?> FindMountPointAsync(string markerFile) =>
        MountPointService == null ? Task.FromResult<string?>(null) : PollAsync(() => MountPointService.FindMountPoint(Device, markerFile));

    /// <summary>
    /// Returns <paramref name="comPort"/> if non-null, or throws <see cref="ComPortNotFoundException"/>.
    /// </summary>
    protected string RequireComPort(string? comPort) =>
        comPort ?? throw new ComPortNotFoundException(Name);
}
