using System.Data;

namespace MultiCut.Data;

/// <summary>
/// Provides read-only database operations for common MultiCut UI lists.
/// </summary>
internal static class MultiCutDatabaseReadOperations
{
    /// <summary>
    /// Loads every current MultiCut as a read-only list projection.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="transaction">An optional active transaction.</param>
    /// <returns>The current MultiCuts ordered for display.</returns>
    public static IReadOnlyList<MultiCutListItem> LoadCurrentMultiCuts(
        IDbConnection connection,
        IDbTransaction? transaction = null)
    {
        using IDbCommand command = CreateCommand(
            connection,
            MultiCutDatabaseQueries.SelectCurrentMultiCuts,
            transaction);
        using IDataReader reader = command.ExecuteReader();

        var multiCuts = new List<MultiCutListItem>();
        while (reader.Read())
        {
            multiCuts.Add(new MultiCutListItem(
                GetInt64(reader, "Id"),
                GetString(reader, "Name"),
                GetString(reader, "JsonPath"),
                GetNullableString(reader, "ShortcutPath"),
                GetNullableString(reader, "IconPath"),
                GetNullableInt32(reader, "IconIndex"),
                GetString(reader, "CreatedAt"),
                GetString(reader, "UpdatedAt"),
                GetInt64(reader, "LaunchTargetCount")));
        }

        return multiCuts;
    }

    /// <summary>
    /// Loads every current reusable launch target as a read-only list projection.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="transaction">An optional active transaction.</param>
    /// <returns>The current launch targets ordered for display.</returns>
    public static IReadOnlyList<LaunchTargetListItem> LoadCurrentLaunchTargets(
        IDbConnection connection,
        IDbTransaction? transaction = null)
    {
        using IDbCommand command = CreateCommand(
            connection,
            MultiCutDatabaseQueries.SelectCurrentLaunchTargets,
            transaction);
        using IDataReader reader = command.ExecuteReader();

        var launchTargets = new List<LaunchTargetListItem>();
        while (reader.Read())
        {
            launchTargets.Add(new LaunchTargetListItem(
                GetInt64(reader, "Id"),
                GetString(reader, "Name"),
                GetString(reader, "Location"),
                GetString(reader, "Arguments"),
                GetString(reader, "CreatedAt"),
                GetString(reader, "UpdatedAt"),
                GetInt64(reader, "MultiCutCount")));
        }

        return launchTargets;
    }

    private static IDbCommand CreateCommand(
        IDbConnection connection,
        string commandText,
        IDbTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IDbCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;
        return command;
    }

    private static string GetString(IDataRecord record, string columnName)
    {
        int ordinal = record.GetOrdinal(columnName);
        return Convert.ToString(record.GetValue(ordinal)) ?? string.Empty;
    }

    private static string? GetNullableString(IDataRecord record, string columnName)
    {
        int ordinal = record.GetOrdinal(columnName);
        return record.IsDBNull(ordinal) ? null : Convert.ToString(record.GetValue(ordinal));
    }

    private static int? GetNullableInt32(IDataRecord record, string columnName)
    {
        int ordinal = record.GetOrdinal(columnName);
        return record.IsDBNull(ordinal) ? null : Convert.ToInt32(record.GetValue(ordinal));
    }

    private static long GetInt64(IDataRecord record, string columnName)
    {
        // SQLite providers often surface INTEGER and COUNT values as Int64, while
        // test doubles may use Int32. Convert keeps the read model provider-tolerant.
        int ordinal = record.GetOrdinal(columnName);
        return Convert.ToInt64(record.GetValue(ordinal));
    }
}
