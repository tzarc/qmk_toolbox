namespace QmkToolbox.Core.Bootloader;

/// <summary>Thrown when the serial port a device is flashed through never appears.</summary>
public class ComPortNotFoundException(string deviceName)
    : IOException($"{deviceName}: COM port not found.");
