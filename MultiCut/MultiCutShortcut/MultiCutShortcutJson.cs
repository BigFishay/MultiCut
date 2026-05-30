using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiCut.Shortcuts;

/// <summary>
/// Serializes and deserializes the JSON contract consumed by MultiEX.
/// </summary>
public static class MultiCutShortcutJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Serializes a MultiCut shortcut contract.
    /// </summary>
    /// <param name="multiCutShortcut">The shortcut contract to serialize.</param>
    /// <returns>The formatted JSON contract.</returns>
    public static string Serialize(MultiCutShortcut multiCutShortcut)
    {
        ArgumentNullException.ThrowIfNull(multiCutShortcut);

        MultiCutShortcut normalizedShortcut = NormalizeForContract(multiCutShortcut);
        return JsonSerializer.Serialize(normalizedShortcut, WriteOptions);
    }

    /// <summary>
    /// Deserializes a MultiCut shortcut contract.
    /// </summary>
    /// <param name="json">The JSON contract text.</param>
    /// <returns>The deserialized shortcut contract.</returns>
    /// <exception cref="InvalidDataException">Thrown when the JSON root is not a MultiCut shortcut.</exception>
    public static MultiCutShortcut Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("MultiCut JSON cannot be blank.");
        }

        MultiCutShortcut? multiCutShortcut = JsonSerializer.Deserialize<MultiCutShortcut>(json, ReadOptions);
        return multiCutShortcut ?? throw new InvalidDataException("MultiCut JSON cannot be null.");
    }

    /// <summary>
    /// Reads and deserializes a MultiCut shortcut contract from disk.
    /// </summary>
    /// <param name="jsonPath">The JSON file path to read.</param>
    /// <returns>The deserialized shortcut contract.</returns>
    public static MultiCutShortcut ReadFromFile(string jsonPath)
    {
        string json = File.ReadAllText(jsonPath);
        return Deserialize(json);
    }

    /// <summary>
    /// Serializes and writes a MultiCut shortcut contract to disk.
    /// </summary>
    /// <param name="multiCutShortcut">The shortcut contract to write.</param>
    /// <param name="jsonPath">The JSON file path to write.</param>
    /// <param name="overwrite">Whether an existing file can be replaced.</param>
    public static void WriteToFile(MultiCutShortcut multiCutShortcut, string jsonPath, bool overwrite = true)
    {
        string? directory = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!overwrite && File.Exists(jsonPath))
        {
            throw new InvalidOperationException($"The JSON file already exists at '{jsonPath}'.");
        }

        File.WriteAllText(jsonPath, Serialize(multiCutShortcut));
    }

    private static MultiCutShortcut NormalizeForContract(MultiCutShortcut multiCutShortcut)
    {
        string normalizedName = string.IsNullOrWhiteSpace(multiCutShortcut.Name)
            ? "MultiCut"
            : multiCutShortcut.Name.Trim();

        return new MultiCutShortcut(
            normalizedName,
            multiCutShortcut.JsonPath,
            LaunchTargetRules.NormalizeLaunchTargets(multiCutShortcut.LaunchTargets));
    }
}
