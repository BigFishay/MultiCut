namespace MultiCut.Data;

/// <summary>
/// Defines planned SQLite queries for common MultiCut database workflows.
/// </summary>
/// <remarks>
/// These queries document the intended edit/delete behavior before the database service
/// is introduced. UI bridge code should eventually call a repository that uses these shapes.
/// </remarks>
internal static class MultiCutDatabaseQueries
{
    /// <summary>
    /// Gets every current MultiCut for read-only UI list views.
    /// </summary>
    public const string SelectCurrentMultiCuts = """
        SELECT
            mc.Id,
            mc.Name,
            mc.JsonPath,
            mc.ShortcutPath,
            mc.IconPath,
            mc.IconIndex,
            mc.CreatedAt,
            mc.UpdatedAt,
            COUNT(mclt.LaunchTargetId) AS LaunchTargetCount
        FROM MultiCuts AS mc
        LEFT JOIN MultiCutLaunchTargets AS mclt ON mclt.MultiCutId = mc.Id
        GROUP BY
            mc.Id,
            mc.Name,
            mc.JsonPath,
            mc.ShortcutPath,
            mc.IconPath,
            mc.IconIndex,
            mc.CreatedAt,
            mc.UpdatedAt
        ORDER BY mc.Name COLLATE NOCASE, mc.Id;
        """;

    /// <summary>
    /// Gets every current reusable launch target for read-only UI list views.
    /// </summary>
    public const string SelectCurrentLaunchTargets = """
        SELECT
            lt.Id,
            lt.Name,
            lt.Location,
            lt.Arguments,
            lt.CreatedAt,
            lt.UpdatedAt,
            COUNT(mclt.MultiCutId) AS MultiCutCount
        FROM LaunchTargets AS lt
        LEFT JOIN MultiCutLaunchTargets AS mclt ON mclt.LaunchTargetId = lt.Id
        GROUP BY
            lt.Id,
            lt.Name,
            lt.Location,
            lt.Arguments,
            lt.CreatedAt,
            lt.UpdatedAt
        ORDER BY lt.Name COLLATE NOCASE, lt.Location COLLATE NOCASE, lt.Arguments, lt.Id;
        """;

    /// <summary>
    /// Gets the ordered launch targets for one MultiCut so its MultiEX JSON file can be regenerated.
    /// </summary>
    public const string SelectOrderedLaunchTargetsForMultiCut = """
        SELECT
            lt.Id,
            lt.Name,
            lt.Location,
            lt.Arguments,
            mclt.SortOrder
        FROM MultiCutLaunchTargets AS mclt
        INNER JOIN LaunchTargets AS lt ON lt.Id = mclt.LaunchTargetId
        WHERE mclt.MultiCutId = @MultiCutId
        ORDER BY mclt.SortOrder;
        """;

    /// <summary>
    /// Finds every parent MultiCut affected when a reusable launch target changes.
    /// </summary>
    /// <remarks>
    /// After updating a LaunchTargets row, each returned MultiCut should have its JSON file regenerated.
    /// </remarks>
    public const string SelectParentMultiCutsForLaunchTarget = """
        SELECT
            mc.Id,
            mc.Name,
            mc.JsonPath,
            mc.ShortcutPath,
            mc.IconPath,
            mc.IconIndex
        FROM MultiCutLaunchTargets AS mclt
        INNER JOIN MultiCuts AS mc ON mc.Id = mclt.MultiCutId
        WHERE mclt.LaunchTargetId = @LaunchTargetId
        ORDER BY mc.Name;
        """;

    /// <summary>
    /// Removes one launch target from one MultiCut while leaving the reusable target in the catalog.
    /// </summary>
    public const string RemoveLaunchTargetFromMultiCut = """
        DELETE FROM MultiCutLaunchTargets
        WHERE MultiCutId = @MultiCutId
          AND LaunchTargetId = @LaunchTargetId;
        """;

    /// <summary>
    /// Removes all launch target links for a deleted MultiCut before deleting its row.
    /// </summary>
    public const string DeleteMultiCutLinks = """
        DELETE FROM MultiCutLaunchTargets
        WHERE MultiCutId = @MultiCutId;
        """;

    /// <summary>
    /// Finds reusable launch targets that no MultiCut references.
    /// </summary>
    public const string SelectUnusedLaunchTargets = """
        SELECT
            lt.Id,
            lt.Name,
            lt.Location,
            lt.Arguments
        FROM LaunchTargets AS lt
        LEFT JOIN MultiCutLaunchTargets AS mclt ON mclt.LaunchTargetId = lt.Id
        WHERE mclt.LaunchTargetId IS NULL
        ORDER BY lt.Name;
        """;

    /// <summary>
    /// Deletes reusable launch targets that no MultiCut references.
    /// </summary>
    public const string DeleteUnusedLaunchTargets = """
        DELETE FROM LaunchTargets
        WHERE Id NOT IN (
            SELECT DISTINCT LaunchTargetId
            FROM MultiCutLaunchTargets
        );
        """;
}
