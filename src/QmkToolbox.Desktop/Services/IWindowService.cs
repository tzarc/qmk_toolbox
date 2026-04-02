namespace QmkToolbox.Desktop.Services;

public interface IWindowService
{
    Task<string?> PickFirmwareFileAsync();
    Task SetClipboardTextAsync(string text);
    void ShowKeyTester();
    void ShowHidConsole();
    void ShowAbout();
}
