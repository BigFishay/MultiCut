namespace MultiCut.Data;

/// <summary>
/// Defines the SQLite schema for persisted MultiCut metadata.
/// </summary>
/// <remarks>
/// SQLite is the UI source of truth. MultiEX JSON files are generated from these tables.
/// </remarks>
internal static class MultiCutDatabaseSchema
{
    /// <summary>
    /// Creates the table that stores one row per MultiCut shortcut group.
    /// </summary>
    public const string CreateMultiCutsTable = """
        CREATE TABLE IF NOT EXISTS MultiCuts (
            Id INTEGER PRIMARY KEY,
            Name TEXT NOT NULL,
            JsonPath TEXT NOT NULL UNIQUE,
            ShortcutPath TEXT,
            IconPath TEXT,
            IconIndex INTEGER,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );
        """;

    /// <summary>
    /// Creates the reusable launch target catalog.
    /// </summary>
    /// <remarks>
    /// Arguments are stored as an empty string when no arguments are present. SQLite allows
    /// repeated NULL values in UNIQUE constraints, so a non-null normalized value is needed
    /// to enforce the "same Location + Arguments is one launch state" rule.
    /// </remarks>
    public const string CreateLaunchTargetsTable = """
        CREATE TABLE IF NOT EXISTS LaunchTargets (
            Id INTEGER PRIMARY KEY,
            Name TEXT NOT NULL,
            Location TEXT NOT NULL COLLATE NOCASE,
            Arguments TEXT NOT NULL DEFAULT '',
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            UNIQUE(Location, Arguments)
        );
        """;

    /// <summary>
    /// Creates the join table that assigns reusable launch targets to MultiCuts.
    /// </summary>
    /// <remarks>
    /// The composite primary key enforces the set architecture: a MultiCut can include a
    /// launch state once, while the same launch target can still be reused by other MultiCuts.
    /// </remarks>
    public const string CreateMultiCutLaunchTargetsTable = """
        CREATE TABLE IF NOT EXISTS MultiCutLaunchTargets (
            MultiCutId INTEGER NOT NULL,
            LaunchTargetId INTEGER NOT NULL,
            SortOrder INTEGER NOT NULL,
            PRIMARY KEY (MultiCutId, LaunchTargetId),
            FOREIGN KEY (MultiCutId) REFERENCES MultiCuts(Id) ON DELETE CASCADE,
            FOREIGN KEY (LaunchTargetId) REFERENCES LaunchTargets(Id) ON DELETE RESTRICT
        );
        """;

    /// <summary>
    /// Creates supporting indexes for common UI lookup paths.
    /// </summary>
    public const string CreateIndexes = """
        CREATE INDEX IF NOT EXISTS IX_MultiCutLaunchTargets_MultiCutId_SortOrder
            ON MultiCutLaunchTargets(MultiCutId, SortOrder);

        CREATE INDEX IF NOT EXISTS IX_MultiCutLaunchTargets_LaunchTargetId
            ON MultiCutLaunchTargets(LaunchTargetId);
        """;

    /// <summary>
    /// Gets the schema statements in dependency order.
    /// </summary>
    public static IReadOnlyList<string> CreateAllStatements { get; } =
    [
        CreateMultiCutsTable,
        CreateLaunchTargetsTable,
        CreateMultiCutLaunchTargetsTable,
        CreateIndexes
    ];
}
