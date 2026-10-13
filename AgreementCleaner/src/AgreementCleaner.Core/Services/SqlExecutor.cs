using System.Data;
using AgreementCleaner.Core.Models;
using Microsoft.Data.SqlClient;

namespace AgreementCleaner.Core.Services;

/// <summary>A parameter passed to a script, for example @CustomerId.</summary>
public sealed record ScriptParameter(string Name, SqlDbType Type, object? Value, int Size = 0);

/// <summary>
/// Runs SQL scripts. Kept behind an interface so the clean-up order can be
/// unit tested without a real database.
/// </summary>
public interface ISqlExecutor
{
    /// <summary>Runs a script and returns its first result set as a table.</summary>
    Task<DataTable> QueryAsync(string connectionString, string sql, IReadOnlyList<ScriptParameter> parameters,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a delete script and returns the per-table counts from the last
    /// result set that has the columns TableName and RowsAffected.
    /// </summary>
    Task<IReadOnlyList<TableCount>> ExecuteCleanupAsync(string connectionString, string sql,
        IReadOnlyList<ScriptParameter> parameters, CancellationToken cancellationToken);
}

/// <summary>Runs scripts against SQL Server.</summary>
public sealed class SqlServerExecutor : ISqlExecutor
{
    public const string TableNameColumn = "TableName";
    public const string RowsAffectedColumn = "RowsAffected";

    private readonly int _commandTimeoutSeconds;
    private readonly Action<string>? _log;

    /// <param name="commandTimeoutSeconds">How long one script may run.</param>
    /// <param name="log">Receives PRINT messages from the scripts.</param>
    public SqlServerExecutor(int commandTimeoutSeconds, Action<string>? log = null)
    {
        _commandTimeoutSeconds = commandTimeoutSeconds;
        _log = log;
    }

    public async Task<DataTable> QueryAsync(string connectionString, string sql,
        IReadOnlyList<ScriptParameter> parameters, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    public async Task<IReadOnlyList<TableCount>> ExecuteCleanupAsync(string connectionString, string sql,
        IReadOnlyList<ScriptParameter> parameters, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(connectionString, cancellationToken);
        await using var command = CreateCommand(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        List<TableCount>? counts = null;
        do
        {
            if (!HasCountColumns(reader))
                continue;

            // Keep the LAST result set that has the expected columns.
            counts = new List<TableCount>();
            var nameOrdinal = reader.GetOrdinal(TableNameColumn);
            var rowsOrdinal = reader.GetOrdinal(RowsAffectedColumn);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.IsDBNull(nameOrdinal) ? "(unknown)" : Convert.ToString(reader.GetValue(nameOrdinal))!;
                var rows = reader.IsDBNull(rowsOrdinal) ? 0 : Convert.ToInt32(reader.GetValue(rowsOrdinal));
                counts.Add(new TableCount(name, rows));
            }
        } while (await reader.NextResultAsync(cancellationToken));

        if (counts is null)
        {
            // The script ran but did not report its counts. Tell the user rather than show 0.
            _log?.Invoke($"WARNING: the script did not return the columns {TableNameColumn} and {RowsAffectedColumn}, " +
                         "so the number of rows deleted is not known.");
            var total = reader.RecordsAffected;
            counts = new List<TableCount> { new("(script did not report counts)", Math.Max(total, 0)) };
        }

        return counts;
    }

    private static bool HasCountColumns(SqlDataReader reader)
    {
        if (reader.FieldCount == 0)
            return false;

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        return columns.Contains(TableNameColumn, StringComparer.OrdinalIgnoreCase) &&
               columns.Contains(RowsAffectedColumn, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        if (_log is not null)
        {
            connection.InfoMessage += (_, e) => _log("SQL: " + e.Message);
        }

        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        return connection;
    }

    private SqlCommand CreateCommand(SqlConnection connection, string sql, IReadOnlyList<ScriptParameter> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.CommandTimeout = _commandTimeoutSeconds;

        foreach (var p in parameters)
        {
            var parameter = command.Parameters.Add(p.Name, p.Type);
            if (p.Size > 0)
                parameter.Size = p.Size;
            parameter.Value = p.Value ?? DBNull.Value;
        }
        return command;
    }
}
