using Qmk.Usb.Discovery;
using QmkToolbox.Core.Models;
using QmkToolbox.Core.Services;

namespace QmkToolbox.Core.Bootloader;

/// <summary>
/// Abstract base class for all bootloader device implementations.
/// Wraps a <see cref="UsbDeviceInfo"/> and provides common plumbing for flashing,
/// EEPROM operations, reset, and tool invocation.
/// </summary>
public abstract class BootloaderDevice(UsbDeviceInfo device, BootloaderServices services, bool resolvesComPort = false)
{
    public event Action<BootloaderDevice, string, MessageType>? OutputReceived;

    public UsbDeviceInfo Device { get; } = device;
    protected BootloaderServices Services { get; } = services;

    public ushort VendorId => Device.VendorId;
    public ushort ProductId => Device.ProductId;
    public string Driver => Device.Driver;
    public string DevicePath => Device.DevicePath;

    public string PreferredDriver { get; init; } = "";
    public bool IsEepromFlashable { get; init; }
    public bool IsResettable { get; init; }
    public BootloaderType Type { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Background port resolution when constructed with <c>resolvesComPort</c>; null otherwise.</summary>
    protected Task<string?>? ComPortTask { get; } = resolvesComPort ? PollAsync(() => services.SerialPorts?.FindSerialPort(device), services.PollDelayMs) : null;

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
        FlashService.RunToolAsync(toolName, args, Services.ToolProvider, PrintMessage,
            Services.ProcessRunner, Services.TimeProvider);

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
        Services.MountPoints is not { } mounts ? Task.FromResult<string?>(null) : PollAsync(() => mounts.FindMountPoint(Device, markerFile), Services.PollDelayMs);

    /// <summary>
    /// Returns <paramref name="comPort"/> if non-null, or throws <see cref="ComPortNotFoundException"/>.
    /// </summary>
    protected string RequireComPort(string? comPort) =>
        comPort ?? throw new ComPortNotFoundException(Name);
}
