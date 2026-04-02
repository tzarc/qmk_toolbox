namespace QmkToolbox.Core.Bootloader;

/// <summary>Thrown when a serial port cannot be located for a device that requires one.</summary>
public class ComPortNotFoundException(string deviceName)
    : Exception($"{deviceName}: COM port not found.");
