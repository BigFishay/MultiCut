using MultiCut.Shortcuts;

namespace MultiCut.Data;

/// <summary>
/// Read-only database projection for a MultiCut list row.
/// </summary>
/// <param name="Id">The database identifier.</param>
/// <param name="Name">The user-facing MultiCut name.</param>
/// <param name="JsonPath">The absolute JSON path used by MultiEX shortcuts.</param>
/// <param name="ShortcutPath">The optional Windows shortcut path.</param>
/// <param name="IconPath">The optional icon source path.</param>
/// <param name="IconIndex">The optional icon index for executable or DLL icon sources.</param>
/// <param name="CreatedAt">The creation timestamp stored by SQLite.</param>
/// <param name="UpdatedAt">The last update timestamp stored by SQLite.</param>
/// <param name="LaunchTargetCount">The number of launch targets assigned to this MultiCut.</param>
public sealed record MultiCutListItem(
    long Id,
    string Name,
    string JsonPath,
    string? ShortcutPath,
    string? IconPath,
    int? IconIndex,
    string CreatedAt,
    string UpdatedAt,
    long LaunchTargetCount);

/// <summary>
/// Read-only database projection for a reusable launch target list row.
/// </summary>
/// <param name="Id">The database identifier.</param>
/// <param name="Name">The user-facing launch target name.</param>
/// <param name="Location">The executable, shortcut, document, folder, or URL to open.</param>
/// <param name="Arguments">The normalized argument string. Empty means no arguments.</param>
/// <param name="CreatedAt">The creation timestamp stored by SQLite.</param>
/// <param name="UpdatedAt">The last update timestamp stored by SQLite.</param>
/// <param name="MultiCutCount">The number of MultiCuts that use this launch target.</param>
public sealed record LaunchTargetListItem(
    long Id,
    string Name,
    string Location,
    string Arguments,
    string CreatedAt,
    string UpdatedAt,
    long MultiCutCount);

/// <summary>
/// Database projection for one complete MultiCut and its ordered launch targets.
/// </summary>
/// <param name="Id">The database identifier.</param>
/// <param name="Name">The user-facing MultiCut name.</param>
/// <param name="JsonPath">The absolute JSON path used by MultiEX shortcuts.</param>
/// <param name="ShortcutPath">The optional Windows shortcut path.</param>
/// <param name="IconPath">The optional icon source path.</param>
/// <param name="IconIndex">The optional icon index for executable or DLL icon sources.</param>
/// <param name="LaunchTargets">The ordered launch targets assigned to this MultiCut.</param>
public sealed record MultiCutRecord(
    long Id,
    string Name,
    string JsonPath,
    string? ShortcutPath,
    string? IconPath,
    int? IconIndex,
    IReadOnlyList<LaunchTarget> LaunchTargets)
{
    /// <summary>
    /// Converts the database projection into the JSON contract consumed by MultiEX.
    /// </summary>
    /// <returns>A shortcut contract with the record launch targets.</returns>
    public MultiCutShortcut ToShortcutContract()
    {
        return new MultiCutShortcut(Name, JsonPath, LaunchTargets);
    }
}
