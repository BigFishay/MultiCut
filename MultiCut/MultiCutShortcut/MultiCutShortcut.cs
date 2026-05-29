using System.Text.Json.Serialization;

namespace MultiCut.Shortcuts;

/// <summary>
/// Describes one MultiCut shortcut group as stored in the JSON file consumed by MultiEX.
/// </summary>
/// <remarks>
/// This is the v1 launch contract shared by the MultiCut UI and MultiEX. UI-only metadata
/// such as shortcut icon path, database IDs, timestamps, and .lnk paths belongs outside this JSON contract.
/// </remarks>
public sealed class MultiCutShortcut
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutShortcut"/> class.
    /// </summary>
    public MultiCutShortcut()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutShortcut"/> class with launch data.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="jsonPath">The absolute JSON path used by the Windows shortcut.</param>
    /// <param name="launchTargets">The launch targets that MultiEX should open.</param>
    public MultiCutShortcut(string name, string jsonPath, IEnumerable<LaunchTarget> launchTargets)
    {
        Name = name;
        JsonPath = jsonPath;
        LaunchTargets = launchTargets.ToList();
    }

    /// <summary>
    /// Gets or sets the user-facing MultiCut name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to this MultiCut JSON file.
    /// </summary>
    [JsonIgnore]
    public string JsonPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ordered set of launch targets MultiEX should open.
    /// </summary>
    public List<LaunchTarget> LaunchTargets { get; set; } = [];
}
