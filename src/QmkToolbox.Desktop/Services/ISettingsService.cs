namespace QmkToolbox.Desktop.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
}
