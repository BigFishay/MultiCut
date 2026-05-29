using System.IO;
using System.Runtime.InteropServices;
using MultiCut.Shortcuts;

namespace MultiCut.Services;

/// <summary>
/// Creates Windows shortcut files that launch MultiEX with a MultiCut JSON path.
/// </summary>
public sealed class ShortcutCreationService
{
    /// <summary>
    /// Creates or replaces a Windows .lnk shortcut for a MultiCut.
    /// </summary>
    /// <param name="multiCut">The MultiCut whose JSON file should be passed to MultiEX.</param>
    /// <param name="shortcutPath">The .lnk file path to create.</param>
    /// <param name="multiExPath">The path to MultiEX.exe.</param>
    /// <param name="iconPath">An optional icon source path for the shortcut.</param>
    /// <param name="iconIndex">An optional icon index for executable or DLL icon sources.</param>
    public void CreateShortcut(
        MultiCutShortcut multiCut,
        string shortcutPath,
        string multiExPath,
        string? iconPath = null,
        int? iconIndex = null)
    {
        ArgumentNullException.ThrowIfNull(multiCut);

        string absoluteShortcutPath = NormalizeShortcutPath(shortcutPath);
        string absoluteMultiCutJsonPath = NormalizeExistingAbsoluteJsonPath(
            multiCut.JsonPath,
            nameof(multiCut.JsonPath));
        string absoluteMultiExPath = NormalizeRequiredExecutablePath(multiExPath, nameof(multiExPath));
        string? shortcutDirectory = Path.GetDirectoryName(absoluteShortcutPath);
        if (!string.IsNullOrWhiteSpace(shortcutDirectory))
        {
            Directory.CreateDirectory(shortcutDirectory);
        }

        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new NotSupportedException("Windows Script Host is required to create shortcuts.");

            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Unable to create a Windows shortcut shell object.");

            // WScript.Shell creates the same kind of .lnk file Explorer creates. The shortcut
            // target is MultiEX; the quoted argument is the absolute MultiCut JSON path.
            dynamic shell = shellObject;
            dynamic shortcut = shell.CreateShortcut(absoluteShortcutPath);
            shortcutObject = shortcut;

            shortcut.TargetPath = absoluteMultiExPath;
            shortcut.Arguments = QuoteArgument(absoluteMultiCutJsonPath);
            shortcut.WorkingDirectory = Path.GetDirectoryName(absoluteMultiExPath) ?? string.Empty;

            string? iconLocation = BuildIconLocation(iconPath, iconIndex);
            if (!string.IsNullOrWhiteSpace(iconLocation))
            {
                shortcut.IconLocation = iconLocation;
            }

            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }
    }

    private static string NormalizeShortcutPath(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath))
        {
            throw new ArgumentException("Shortcut path cannot be blank.", nameof(shortcutPath));
        }

        string absoluteShortcutPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(shortcutPath.Trim().Trim('"')));

        if (!string.Equals(Path.GetExtension(absoluteShortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Shortcut path must end with .lnk.", nameof(shortcutPath));
        }

        return absoluteShortcutPath;
    }

    private static string NormalizeExistingAbsoluteJsonPath(string jsonPath, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            throw new ArgumentException("MultiCut JSON path cannot be blank.", parameterName);
        }

        string expandedJsonPath = Environment.ExpandEnvironmentVariables(jsonPath.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expandedJsonPath))
        {
            throw new ArgumentException("MultiCut JSON path must be absolute.", parameterName);
        }

        string absoluteJsonPath = Path.GetFullPath(expandedJsonPath);
        if (!File.Exists(absoluteJsonPath))
        {
            throw new FileNotFoundException("MultiCut JSON file was not found.", absoluteJsonPath);
        }

        return absoluteJsonPath;
    }

    private static string NormalizeRequiredExecutablePath(string filePath, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be blank.", parameterName);
        }

        string absoluteFilePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(filePath.Trim().Trim('"')));
        if (!string.Equals(Path.GetExtension(absoluteFilePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("MultiEX path must point to an .exe file.", parameterName);
        }

        if (!File.Exists(absoluteFilePath))
        {
            throw new FileNotFoundException("Required file was not found.", absoluteFilePath);
        }

        return absoluteFilePath;
    }

    private static string? BuildIconLocation(string? iconPath, int? iconIndex)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        string absoluteIconPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(iconPath.Trim().Trim('"')));

        return iconIndex.HasValue
            ? $"{absoluteIconPath},{iconIndex.Value}"
            : absoluteIconPath;
    }

    private static string QuoteArgument(string argument)
    {
        return $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
