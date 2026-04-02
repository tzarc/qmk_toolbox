namespace QmkToolbox.Core.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
}
