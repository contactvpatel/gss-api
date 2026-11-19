using Npgsql;

namespace GSS.IntegrationTests.Fixtures;

/// <summary>
/// Truncates every table in the application schema, so feature tables are reset without editing a hard-coded list.
/// </summary>
internal static class TestDatabaseReset
{
    private const string Schema = "gss";

    private const string TableQuery = """
        SELECT tablename
        FROM pg_tables
        WHERE schemaname = @schema
          AND tablename <> 'schemaversions'
        ORDER BY tablename
        """;

    /// <summary>Truncates all <c>gss</c> tables and restarts identities. Retries on deadlock (40P01).</summary>
    public static async Task ResetAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await using NpgsqlConnection connection = new(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                List<string> tables = [];
                await using (NpgsqlCommand tableCommand = new(TableQuery, connection))
                {
                    tableCommand.Parameters.AddWithValue("schema", Schema);
                    await using NpgsqlDataReader reader = await tableCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        tables.Add(reader.GetString(0));
                    }
                }

                if (tables.Count == 0)
                {
                    return;
                }

                string qualifiedTables = string.Join(", ", tables.Select(table => $"{Quote(Schema)}.{Quote(table)}"));
                await using NpgsqlCommand truncateCommand = new($"TRUNCATE TABLE {qualifiedTables} RESTART IDENTITY CASCADE", connection);
                await truncateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == "40P01" && attempt < 3)
            {
                await Task.Delay(100 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
