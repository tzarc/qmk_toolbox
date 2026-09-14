namespace QmkToolbox.Core.Services;

/// <summary>
/// Extracts bundled flash tool binaries and data files and resolves their paths.
/// </summary>
public interface IFlashToolProvider
{
    /// <summary>Returns the absolute path to the named tool binary. Tool names carry no extension.</summary>
    string GetToolPath(string toolName);

    /// <summary>Returns the absolute path to the named bundled data file (e.g. an EEPROM image or a driver list).</summary>
    string GetDataFilePath(string fileName);

    /// <summary>Returns the absolute path to the local resource folder where tools are extracted.</summary>
    string GetResourceFolder();

    /// <summary>
    /// Ensures the resource folder is present and up to date. When any installed manifest's
    /// commit date differs from its embedded copy, wipes the folder and re-extracts everything;
    /// otherwise re-extracts only the missing files.
    /// </summary>
    void EnsureResourceFolder();

    /// <summary>Clears the resource folder and re-extracts all bundled resources.</summary>
    void ClearAndReExtract();

    /// <summary>One human-readable line describing the installed resource versions, for the startup banner.</summary>
    string DescribeVersions();
}
