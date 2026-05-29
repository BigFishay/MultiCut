using System.IO;
using System.Text;

namespace MultiCut.Services;

/// <summary>
/// Owns default folders, filename creation, and path normalization for the MultiCut UI.
/// </summary>
public sealed class MultiCutPathService
{
    private const string AppFolderName = "MultiCut";
    private const string ShortcutContractFolderName = "Shortcuts";
    private const string MultiExExecutableName = "MultiEX.exe";

    /// <summary>
    /// Gets the default directory for MultiCut JSON files.
    /// </summary>
    public string DefaultJsonDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName,
        ShortcutContractFolderName);

    /// <summary>
    /// Gets the default directory for Windows .lnk files created by the UI.
    /// </summary>
    public string DefaultShortcutDirectory { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// Builds the default JSON path for a MultiCut name.
    /// </summary>
    /// <param name="multiCutName">The user-facing MultiCut name.</param>
    /// <returns>An absolute JSON path in the default MultiCut data folder.</returns>
    public string GetDefaultJsonPath(string multiCutName)
    {
        return Path.Combine(DefaultJsonDirectory, $"{GetSafeFileName(multiCutName)}.json");
    }

    /// <summary>
    /// Builds the default Windows shortcut path for a MultiCut name.
    /// </summary>
    /// <param name="multiCutName">The user-facing MultiCut name.</param>
    /// <returns>An absolute .lnk path in the default shortcut folder.</returns>
    public string GetDefaultShortcutPath(string multiCutName)
    {
        return Path.Combine(DefaultShortcutDirectory, $"{GetSafeFileName(multiCutName)}.lnk");
    }

    /// <summary>
    /// Normalizes a JSON file path.
    /// </summary>
    /// <param name="jsonPath">The JSON path to normalize.</param>
    /// <returns>The absolute JSON path.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is blank or does not end with .json.</exception>
    public string NormalizeJsonPath(string jsonPath)
    {
        string absolutePath = NormalizePath(jsonPath, nameof(jsonPath));
        if (!string.Equals(Path.GetExtension(absolutePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("MultiCut JSON path must end with .json.", nameof(jsonPath));
        }

        return absolutePath;
    }

    /// <summary>
    /// Normalizes a directory path.
    /// </summary>
    /// <param name="directoryPath">The directory path to normalize.</param>
    /// <returns>The absolute directory path.</returns>
    public string NormalizeDirectoryPath(string directoryPath)
    {
        return NormalizePath(directoryPath, nameof(directoryPath));
    }

    /// <summary>
    /// Normalizes a Windows shortcut path.
    /// </summary>
    /// <param name="shortcutPath">The shortcut path to normalize.</param>
    /// <returns>The absolute .lnk path.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is blank or does not end with .lnk.</exception>
    public string NormalizeShortcutPath(string shortcutPath)
    {
        string absolutePath = NormalizePath(shortcutPath, nameof(shortcutPath));
        if (!string.Equals(Path.GetExtension(absolutePath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Shortcut path must end with .lnk.", nameof(shortcutPath));
        }

        return absolutePath;
    }

    /// <summary>
    /// Resolves the MultiEX executable path used by Windows shortcuts.
    /// </summary>
    /// <param name="configuredPath">An optional explicit MultiEX.exe path.</param>
    /// <returns>The absolute MultiEX.exe path.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no usable MultiEX.exe path is available.</exception>
    public string ResolveMultiExPath(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return NormalizeExistingExecutablePath(configuredPath, nameof(configuredPath));
        }

        string adjacentPath = Path.Combine(AppContext.BaseDirectory, MultiExExecutableName);
        if (File.Exists(adjacentPath))
        {
            return adjacentPath;
        }

        throw new InvalidOperationException(
            "MultiEX.exe path is not configured and could not be found next to the MultiCut application.");
    }

    /// <summary>
    /// Normalizes a required executable path.
    /// </summary>
    /// <param name="filePath">The executable path to normalize.</param>
    /// <param name="parameterName">The parameter name for validation errors.</param>
    /// <returns>The absolute executable path.</returns>
    public string NormalizeExistingExecutablePath(string filePath, string parameterName)
    {
        string absolutePath = NormalizePath(filePath, parameterName);
        if (!string.Equals(Path.GetExtension(absolutePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Executable path must point to an .exe file.", parameterName);
        }

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Executable file was not found.", absolutePath);
        }

        return absolutePath;
    }

    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be blank.", parameterName);
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
    }

    private static string GetSafeFileName(string displayName)
    {
        string trimmedName = string.IsNullOrWhiteSpace(displayName)
            ? AppFolderName
            : displayName.Trim();

        char[] invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(trimmedName.Length);
        foreach (char character in trimmedName)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        string safeName = builder.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(safeName) ? AppFolderName : safeName;
    }
}
