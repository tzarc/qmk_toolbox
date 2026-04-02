namespace QmkToolbox.Core.Services;

public interface IFlashToolProvider
{
    string GetToolPath(string toolName);
    string GetResourceFolder();
    void EnsureResourceFolder();
    void ClearResourceFolder();
    void ExtractResource(string file);
    void ExtractAllResources();
}
