using System.IO;
using Microsoft.Data.Sqlite;
using MultiCut.Shortcuts;

namespace MultiCut.Data;

/// <summary>
/// Stores MultiCuts, reusable launch targets, and their relationships in SQLite.
/// </summary>
public sealed class MultiCutRepository
{
    private readonly string databasePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiCutRepository"/> class.
    /// </summary>
    /// <param name="databasePath">The SQLite database file path.</param>
    public MultiCutRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be blank.", nameof(databasePath));
        }

        this.databasePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(databasePath.Trim().Trim('"')));
    }

    /// <summary>
    /// Gets the SQLite database file path.
    /// </summary>
    public string DatabasePath => databasePath;

    /// <summary>
    /// Creates the database file and schema when they do not already exist.
    /// </summary>
    public void Initialize()
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using SqliteConnection connection = OpenConnection();
        foreach (string statement in MultiCutDatabaseSchema.CreateAllStatements)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Gets every current MultiCut as a read-only list projection.
    /// </summary>
    /// <returns>The current MultiCuts ordered for display.</returns>
    public IReadOnlyList<MultiCutListItem> GetCurrentMultiCuts()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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

        using SqliteDataReader reader = command.ExecuteReader();
        var multiCuts = new List<MultiCutListItem>();
        while (reader.Read())
        {
            multiCuts.Add(new MultiCutListItem(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetString(reader.GetOrdinal("Name")),
                reader.GetString(reader.GetOrdinal("JsonPath")),
                GetNullableString(reader, "ShortcutPath"),
                GetNullableString(reader, "IconPath"),
                GetNullableInt32(reader, "IconIndex"),
                reader.GetString(reader.GetOrdinal("CreatedAt")),
                reader.GetString(reader.GetOrdinal("UpdatedAt")),
                reader.GetInt64(reader.GetOrdinal("LaunchTargetCount"))));
        }

        return multiCuts;
    }

    /// <summary>
    /// Gets every current reusable launch target as a read-only list projection.
    /// </summary>
    /// <returns>The current launch targets ordered for display.</returns>
    public IReadOnlyList<LaunchTargetListItem> GetCurrentLaunchTargets()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
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

        using SqliteDataReader reader = command.ExecuteReader();
        var launchTargets = new List<LaunchTargetListItem>();
        while (reader.Read())
        {
            launchTargets.Add(new LaunchTargetListItem(
                reader.GetInt64(reader.GetOrdinal("Id")),
                reader.GetString(reader.GetOrdinal("Name")),
                reader.GetString(reader.GetOrdinal("Location")),
                reader.GetString(reader.GetOrdinal("Arguments")),
                reader.GetString(reader.GetOrdinal("CreatedAt")),
                reader.GetString(reader.GetOrdinal("UpdatedAt")),
                reader.GetInt64(reader.GetOrdinal("MultiCutCount"))));
        }

        return launchTargets;
    }

    /// <summary>
    /// Creates or replaces a MultiCut and its launch target relationships.
    /// </summary>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="jsonPath">The absolute JSON path used by MultiEX shortcuts.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="shortcutPath">The optional Windows shortcut path.</param>
    /// <param name="iconPath">The optional icon source path.</param>
    /// <param name="iconIndex">The optional icon index.</param>
    /// <param name="overwrite">Whether an existing MultiCut at the same JSON path can be replaced.</param>
    /// <returns>The saved MultiCut record.</returns>
    public MultiCutRecord SaveMultiCut(
        string name,
        string jsonPath,
        IEnumerable<LaunchTarget> launchTargets,
        string? shortcutPath = null,
        string? iconPath = null,
        int? iconIndex = null,
        bool overwrite = false)
    {
        string normalizedName = NormalizeName(name);
        string normalizedJsonPath = NormalizePath(jsonPath, nameof(jsonPath));
        List<LaunchTarget> normalizedLaunchTargets = LaunchTargetRules.NormalizeLaunchTargets(launchTargets);
        string? normalizedShortcutPath = NormalizeOptionalPath(shortcutPath);
        string? normalizedIconPath = NormalizeOptionalPath(iconPath);
        string timestamp = DateTimeOffset.UtcNow.ToString("O");

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        long? existingId = GetMultiCutIdByJsonPath(connection, transaction, normalizedJsonPath);
        long multiCutId;
        if (existingId.HasValue)
        {
            if (!overwrite)
            {
                throw new InvalidOperationException($"A MultiCut already exists for '{normalizedJsonPath}'.");
            }

            multiCutId = existingId.Value;
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE MultiCuts
                SET Name = $Name,
                    ShortcutPath = $ShortcutPath,
                    IconPath = $IconPath,
                    IconIndex = $IconIndex,
                    UpdatedAt = $UpdatedAt
                WHERE Id = $Id;
                """,
                ("$Name", normalizedName),
                ("$ShortcutPath", normalizedShortcutPath),
                ("$IconPath", normalizedIconPath),
                ("$IconIndex", iconIndex),
                ("$UpdatedAt", timestamp),
                ("$Id", multiCutId));

            ExecuteNonQuery(
                connection,
                transaction,
                "DELETE FROM MultiCutLaunchTargets WHERE MultiCutId = $MultiCutId;",
                ("$MultiCutId", multiCutId));
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO MultiCuts
                    (Name, JsonPath, ShortcutPath, IconPath, IconIndex, CreatedAt, UpdatedAt)
                VALUES
                    ($Name, $JsonPath, $ShortcutPath, $IconPath, $IconIndex, $CreatedAt, $UpdatedAt);
                """,
                ("$Name", normalizedName),
                ("$JsonPath", normalizedJsonPath),
                ("$ShortcutPath", normalizedShortcutPath),
                ("$IconPath", normalizedIconPath),
                ("$IconIndex", iconIndex),
                ("$CreatedAt", timestamp),
                ("$UpdatedAt", timestamp));

            multiCutId = GetLastInsertId(connection, transaction);
        }

        for (int index = 0; index < normalizedLaunchTargets.Count; index++)
        {
            long launchTargetId = UpsertLaunchTarget(connection, transaction, normalizedLaunchTargets[index], timestamp);
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO MultiCutLaunchTargets (MultiCutId, LaunchTargetId, SortOrder)
                VALUES ($MultiCutId, $LaunchTargetId, $SortOrder);
                """,
                ("$MultiCutId", multiCutId),
                ("$LaunchTargetId", launchTargetId),
                ("$SortOrder", index));
        }

        DeleteUnusedLaunchTargets(connection, transaction);
        MultiCutRecord multiCutRecord = GetMultiCut(connection, transaction, multiCutId);
        transaction.Commit();
        return multiCutRecord;
    }

    /// <summary>
    /// Updates an existing MultiCut by database identifier and replaces its launch target set.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="name">The user-facing MultiCut name.</param>
    /// <param name="launchTargets">The ordered launch targets assigned to the MultiCut.</param>
    /// <param name="jsonPath">An optional replacement JSON path. When omitted, the current path is kept.</param>
    /// <returns>The updated MultiCut record.</returns>
    public MultiCutRecord UpdateMultiCut(
        long multiCutId,
        string name,
        IEnumerable<LaunchTarget> launchTargets,
        string? jsonPath = null)
    {
        string normalizedName = NormalizeName(name);
        List<LaunchTarget> normalizedLaunchTargets = LaunchTargetRules.NormalizeLaunchTargets(launchTargets);
        string timestamp = DateTimeOffset.UtcNow.ToString("O");

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        MultiCutRecord existingMultiCut = GetMultiCut(connection, transaction, multiCutId);
        string normalizedJsonPath = string.IsNullOrWhiteSpace(jsonPath)
            ? existingMultiCut.JsonPath
            : NormalizePath(jsonPath, nameof(jsonPath));

        long? conflictingId = GetMultiCutIdByJsonPath(connection, transaction, normalizedJsonPath);
        if (conflictingId.HasValue && conflictingId.Value != multiCutId)
        {
            throw new InvalidOperationException($"Another MultiCut already uses '{normalizedJsonPath}'.");
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE MultiCuts
            SET Name = $Name,
                JsonPath = $JsonPath,
                UpdatedAt = $UpdatedAt
            WHERE Id = $Id;
            """,
            ("$Name", normalizedName),
            ("$JsonPath", normalizedJsonPath),
            ("$UpdatedAt", timestamp),
            ("$Id", multiCutId));

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM MultiCutLaunchTargets WHERE MultiCutId = $MultiCutId;",
            ("$MultiCutId", multiCutId));

        for (int index = 0; index < normalizedLaunchTargets.Count; index++)
        {
            long launchTargetId = UpsertLaunchTarget(connection, transaction, normalizedLaunchTargets[index], timestamp);
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO MultiCutLaunchTargets (MultiCutId, LaunchTargetId, SortOrder)
                VALUES ($MultiCutId, $LaunchTargetId, $SortOrder);
                """,
                ("$MultiCutId", multiCutId),
                ("$LaunchTargetId", launchTargetId),
                ("$SortOrder", index));
        }

        DeleteUnusedLaunchTargets(connection, transaction);
        MultiCutRecord multiCutRecord = GetMultiCut(connection, transaction, multiCutId);
        transaction.Commit();
        return multiCutRecord;
    }

    /// <summary>
    /// Gets one complete MultiCut by database identifier.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <returns>The matching MultiCut record.</returns>
    public MultiCutRecord GetMultiCut(long multiCutId)
    {
        using SqliteConnection connection = OpenConnection();
        return GetMultiCut(connection, null, multiCutId);
    }

    /// <summary>
    /// Gets one complete MultiCut by JSON path.
    /// </summary>
    /// <param name="jsonPath">The JSON path to look up.</param>
    /// <returns>The matching MultiCut record, or <see langword="null"/> when none exists.</returns>
    public MultiCutRecord? TryGetMultiCutByJsonPath(string jsonPath)
    {
        string normalizedJsonPath = NormalizePath(jsonPath, nameof(jsonPath));
        using SqliteConnection connection = OpenConnection();
        long? multiCutId = GetMultiCutIdByJsonPath(connection, null, normalizedJsonPath);
        return multiCutId.HasValue ? GetMultiCut(connection, null, multiCutId.Value) : null;
    }

    /// <summary>
    /// Updates shortcut metadata for an existing MultiCut.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <param name="shortcutPath">The Windows shortcut path.</param>
    /// <param name="iconPath">The optional icon source path.</param>
    /// <param name="iconIndex">The optional icon index.</param>
    public void UpdateShortcutMetadata(long multiCutId, string shortcutPath, string? iconPath, int? iconIndex)
    {
        string normalizedShortcutPath = NormalizePath(shortcutPath, nameof(shortcutPath));
        string? normalizedIconPath = NormalizeOptionalPath(iconPath);
        using SqliteConnection connection = OpenConnection();
        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE MultiCuts
            SET ShortcutPath = $ShortcutPath,
                IconPath = $IconPath,
                IconIndex = $IconIndex,
                UpdatedAt = $UpdatedAt
            WHERE Id = $Id;
            """,
            ("$ShortcutPath", normalizedShortcutPath),
            ("$IconPath", normalizedIconPath),
            ("$IconIndex", iconIndex),
            ("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O")),
            ("$Id", multiCutId));
    }

    /// <summary>
    /// Deletes a MultiCut and its relationships.
    /// </summary>
    /// <param name="multiCutId">The MultiCut database identifier.</param>
    /// <returns>The deleted MultiCut record, or <see langword="null"/> when no row existed.</returns>
    public MultiCutRecord? DeleteMultiCut(long multiCutId)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        MultiCutRecord? existingRecord = TryGetMultiCut(connection, transaction, multiCutId);
        if (existingRecord is null)
        {
            transaction.Commit();
            return null;
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM MultiCuts WHERE Id = $Id;",
            ("$Id", multiCutId));
        DeleteUnusedLaunchTargets(connection, transaction);

        transaction.Commit();
        return existingRecord;
    }

    /// <summary>
    /// Updates one reusable launch target and returns every affected parent MultiCut.
    /// </summary>
    /// <param name="launchTargetId">The launch target database identifier.</param>
    /// <param name="launchTarget">The updated launch target values.</param>
    /// <returns>The parent MultiCuts whose generated JSON should be rewritten.</returns>
    public IReadOnlyList<MultiCutRecord> UpdateLaunchTarget(long launchTargetId, LaunchTarget launchTarget)
    {
        LaunchTarget normalizedLaunchTarget = LaunchTargetRules.NormalizeLaunchTarget(launchTarget);
        string normalizedArguments = LaunchTargetRules.NormalizeArgumentsForStorage(normalizedLaunchTarget.Arguments);
        string timestamp = DateTimeOffset.UtcNow.ToString("O");

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();
        long[] parentIds = GetParentMultiCutIds(connection, transaction, launchTargetId);
        if (!LaunchTargetExists(connection, transaction, launchTargetId))
        {
            throw new InvalidOperationException($"Launch target '{launchTargetId}' was not found.");
        }

        long? matchingLaunchTargetId = GetLaunchTargetIdByState(
            connection,
            transaction,
            normalizedLaunchTarget.Location,
            normalizedArguments);
        var affectedParentIds = new HashSet<long>(parentIds);

        if (matchingLaunchTargetId.HasValue && matchingLaunchTargetId.Value != launchTargetId)
        {
            long mergeTargetId = matchingLaunchTargetId.Value;
            foreach (long parentId in GetParentMultiCutIds(connection, transaction, mergeTargetId))
            {
                affectedParentIds.Add(parentId);
            }

            MergeLaunchTargetIntoExisting(
                connection,
                transaction,
                launchTargetId,
                mergeTargetId,
                normalizedLaunchTarget.Name,
                timestamp);
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE LaunchTargets
                SET Name = $Name,
                    Location = $Location,
                    Arguments = $Arguments,
                    UpdatedAt = $UpdatedAt
                WHERE Id = $Id;
                """,
                ("$Name", normalizedLaunchTarget.Name),
                ("$Location", normalizedLaunchTarget.Location),
                ("$Arguments", normalizedArguments),
                ("$UpdatedAt", timestamp),
                ("$Id", launchTargetId));
        }

        var affectedMultiCuts = new List<MultiCutRecord>();
        foreach (long parentId in affectedParentIds.Order())
        {
            NormalizeSortOrder(connection, transaction, parentId);
            affectedMultiCuts.Add(GetMultiCut(connection, transaction, parentId));
        }

        DeleteUnusedLaunchTargets(connection, transaction);
        transaction.Commit();
        return affectedMultiCuts;
    }

    private SqliteConnection OpenConnection()
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        };
        var connection = new SqliteConnection(connectionStringBuilder.ToString());
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static MultiCutRecord GetMultiCut(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long multiCutId)
    {
        return TryGetMultiCut(connection, transaction, multiCutId)
            ?? throw new InvalidOperationException($"MultiCut '{multiCutId}' was not found.");
    }

    private static MultiCutRecord? TryGetMultiCut(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long multiCutId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, Name, JsonPath, ShortcutPath, IconPath, IconIndex
            FROM MultiCuts
            WHERE Id = $Id;
            """;
        command.Parameters.AddWithValue("$Id", multiCutId);

        long id;
        string name;
        string jsonPath;
        string? shortcutPath;
        string? iconPath;
        int? iconIndex;
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            id = reader.GetInt64(reader.GetOrdinal("Id"));
            name = reader.GetString(reader.GetOrdinal("Name"));
            jsonPath = reader.GetString(reader.GetOrdinal("JsonPath"));
            shortcutPath = GetNullableString(reader, "ShortcutPath");
            iconPath = GetNullableString(reader, "IconPath");
            iconIndex = GetNullableInt32(reader, "IconIndex");
        }

        return new MultiCutRecord(
            id,
            name,
            jsonPath,
            shortcutPath,
            iconPath,
            iconIndex,
            GetOrderedLaunchTargets(connection, transaction, multiCutId));
    }

    private static IReadOnlyList<LaunchTarget> GetOrderedLaunchTargets(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long multiCutId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT lt.Name, lt.Location, lt.Arguments
            FROM MultiCutLaunchTargets AS mclt
            INNER JOIN LaunchTargets AS lt ON lt.Id = mclt.LaunchTargetId
            WHERE mclt.MultiCutId = $MultiCutId
            ORDER BY mclt.SortOrder;
            """;
        command.Parameters.AddWithValue("$MultiCutId", multiCutId);

        using SqliteDataReader reader = command.ExecuteReader();
        var launchTargets = new List<LaunchTarget>();
        while (reader.Read())
        {
            string arguments = reader.GetString(reader.GetOrdinal("Arguments"));
            launchTargets.Add(new LaunchTarget(
                reader.GetString(reader.GetOrdinal("Name")),
                reader.GetString(reader.GetOrdinal("Location")),
                string.IsNullOrWhiteSpace(arguments) ? null : arguments));
        }

        return launchTargets;
    }

    private static long UpsertLaunchTarget(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LaunchTarget launchTarget,
        string timestamp)
    {
        string normalizedArguments = LaunchTargetRules.NormalizeArgumentsForStorage(launchTarget.Arguments);
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO LaunchTargets (Name, Location, Arguments, CreatedAt, UpdatedAt)
            VALUES ($Name, $Location, $Arguments, $CreatedAt, $UpdatedAt)
            ON CONFLICT(Location, Arguments) DO UPDATE SET
                Name = excluded.Name,
                UpdatedAt = excluded.UpdatedAt;
            """,
            ("$Name", launchTarget.Name),
            ("$Location", launchTarget.Location),
            ("$Arguments", normalizedArguments),
            ("$CreatedAt", timestamp),
            ("$UpdatedAt", timestamp));

        object? result = ExecuteScalar(
            connection,
            transaction,
            """
            SELECT Id
            FROM LaunchTargets
            WHERE Location = $Location COLLATE NOCASE
              AND Arguments = $Arguments
            LIMIT 1;
            """,
            ("$Location", launchTarget.Location),
            ("$Arguments", normalizedArguments));

        return Convert.ToInt64(result);
    }

    private static void MergeLaunchTargetIntoExisting(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long sourceLaunchTargetId,
        long mergeTargetId,
        string mergedName,
        string timestamp)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE LaunchTargets
            SET Name = $Name,
                UpdatedAt = $UpdatedAt
            WHERE Id = $Id;
            """,
            ("$Name", mergedName),
            ("$UpdatedAt", timestamp),
            ("$Id", mergeTargetId));

        foreach (long parentId in GetParentMultiCutIds(connection, transaction, sourceLaunchTargetId))
        {
            int? sourceSortOrder = GetRelationshipSortOrder(
                connection,
                transaction,
                parentId,
                sourceLaunchTargetId);
            int? targetSortOrder = GetRelationshipSortOrder(
                connection,
                transaction,
                parentId,
                mergeTargetId);

            if (targetSortOrder.HasValue)
            {
                int mergedSortOrder = sourceSortOrder.HasValue
                    ? Math.Min(sourceSortOrder.Value, targetSortOrder.Value)
                    : targetSortOrder.Value;
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE MultiCutLaunchTargets
                    SET SortOrder = $SortOrder
                    WHERE MultiCutId = $MultiCutId
                      AND LaunchTargetId = $LaunchTargetId;
                    """,
                    ("$SortOrder", mergedSortOrder),
                    ("$MultiCutId", parentId),
                    ("$LaunchTargetId", mergeTargetId));
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    DELETE FROM MultiCutLaunchTargets
                    WHERE MultiCutId = $MultiCutId
                      AND LaunchTargetId = $LaunchTargetId;
                    """,
                    ("$MultiCutId", parentId),
                    ("$LaunchTargetId", sourceLaunchTargetId));
            }
            else
            {
                ExecuteNonQuery(
                    connection,
                    transaction,
                    """
                    UPDATE MultiCutLaunchTargets
                    SET LaunchTargetId = $MergeTargetId
                    WHERE MultiCutId = $MultiCutId
                      AND LaunchTargetId = $SourceLaunchTargetId;
                    """,
                    ("$MergeTargetId", mergeTargetId),
                    ("$MultiCutId", parentId),
                    ("$SourceLaunchTargetId", sourceLaunchTargetId));
            }
        }

        ExecuteNonQuery(
            connection,
            transaction,
            "DELETE FROM LaunchTargets WHERE Id = $Id;",
            ("$Id", sourceLaunchTargetId));
    }

    private static void NormalizeSortOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long multiCutId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT LaunchTargetId
            FROM MultiCutLaunchTargets
            WHERE MultiCutId = $MultiCutId
            ORDER BY SortOrder, LaunchTargetId;
            """;
        command.Parameters.AddWithValue("$MultiCutId", multiCutId);

        var orderedLaunchTargetIds = new List<long>();
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                orderedLaunchTargetIds.Add(reader.GetInt64(0));
            }
        }

        for (int index = 0; index < orderedLaunchTargetIds.Count; index++)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE MultiCutLaunchTargets
                SET SortOrder = $SortOrder
                WHERE MultiCutId = $MultiCutId
                  AND LaunchTargetId = $LaunchTargetId;
                """,
                ("$SortOrder", index),
                ("$MultiCutId", multiCutId),
                ("$LaunchTargetId", orderedLaunchTargetIds[index]));
        }
    }

    private static int? GetRelationshipSortOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long multiCutId,
        long launchTargetId)
    {
        object? result = ExecuteScalar(
            connection,
            transaction,
            """
            SELECT SortOrder
            FROM MultiCutLaunchTargets
            WHERE MultiCutId = $MultiCutId
              AND LaunchTargetId = $LaunchTargetId
            LIMIT 1;
            """,
            ("$MultiCutId", multiCutId),
            ("$LaunchTargetId", launchTargetId));
        return result is null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private static long? GetLaunchTargetIdByState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string location,
        string arguments)
    {
        object? result = ExecuteScalar(
            connection,
            transaction,
            """
            SELECT Id
            FROM LaunchTargets
            WHERE Location = $Location COLLATE NOCASE
              AND Arguments = $Arguments
            LIMIT 1;
            """,
            ("$Location", location),
            ("$Arguments", arguments));
        return result is null || result == DBNull.Value ? null : Convert.ToInt64(result);
    }

    private static bool LaunchTargetExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long launchTargetId)
    {
        object? result = ExecuteScalar(
            connection,
            transaction,
            "SELECT 1 FROM LaunchTargets WHERE Id = $Id LIMIT 1;",
            ("$Id", launchTargetId));
        return result is not null && result != DBNull.Value;
    }

    private static long? GetMultiCutIdByJsonPath(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string jsonPath)
    {
        object? result = ExecuteScalar(
            connection,
            transaction,
            "SELECT Id FROM MultiCuts WHERE JsonPath = $JsonPath LIMIT 1;",
            ("$JsonPath", jsonPath));
        return result is null || result == DBNull.Value ? null : Convert.ToInt64(result);
    }

    private static long[] GetParentMultiCutIds(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long launchTargetId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MultiCutId
            FROM MultiCutLaunchTargets
            WHERE LaunchTargetId = $LaunchTargetId
            ORDER BY MultiCutId;
            """;
        command.Parameters.AddWithValue("$LaunchTargetId", launchTargetId);

        using SqliteDataReader reader = command.ExecuteReader();
        var parentIds = new List<long>();
        while (reader.Read())
        {
            parentIds.Add(reader.GetInt64(0));
        }

        return parentIds.ToArray();
    }

    private static void DeleteUnusedLaunchTargets(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM LaunchTargets
            WHERE Id NOT IN (
                SELECT DISTINCT LaunchTargetId
                FROM MultiCutLaunchTargets
            );
            """);
    }

    private static long GetLastInsertId(SqliteConnection connection, SqliteTransaction transaction)
    {
        object? result = ExecuteScalar(connection, transaction, "SELECT last_insert_rowid();");
        return Convert.ToInt64(result);
    }

    private static int ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        AddParameters(command, parameters);
        return command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        AddParameters(command, parameters);
        return command.ExecuteScalar();
    }

    private static void AddParameters(
        SqliteCommand command,
        params (string Name, object? Value)[] parameters)
    {
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("MultiCut name cannot be blank.", nameof(name));
        }

        return name.Trim();
    }

    private static string NormalizePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be blank.", parameterName);
        }

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
    }

    private static string? GetNullableString(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }
}
