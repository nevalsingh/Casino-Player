using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using OT.Assessment.Core.Exceptions;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Messaging;

namespace OT.Assessment.Infrastructure.Sql;

public sealed class CasinoWagerBatchWriter(string connectionString) : ICasinoWagerBatchWriter
{
    // Constraint/conversion errors: the data itself is bad, retrying won't help.
    // 2627/2601 unique/PK violation, 547 FK or check constraint, 515 NULL into NOT NULL, 8152/2628 truncation,
    // 8115/8114 arithmetic/type conversion overflow, 245 conversion failed, 241/242 date/datetime conversion.
    private static readonly HashSet<int> DataErrorNumbers =
    [
        2627, 2601, 547, 515, 8152, 2628, 8115, 245, 8114, 241, 242,
    ];

    public async Task<int> WriteAsync(IReadOnlyList<CasinoWagerEvent> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return 0;

        var table = BuildTable(batch);

        try
        {
            return await SqlRetryPipeline.Instance.ExecuteAsync(async ct =>
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(ct);

                var parameters = new DynamicParameters();
                parameters.Add("Wagers", table.AsTableValuedParameter("dbo.CasinoWagerTableType"));

                var command = new CommandDefinition(
                    "dbo.usp_CasinoWager_InsertBatch",
                    parameters,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 60,
                    cancellationToken: ct);

                return await connection.ExecuteScalarAsync<int>(command);
            }, cancellationToken);
        }
        catch (SqlException ex) when (IsDataError(ex))
        {
            throw new NonRetryableWagerDataException(
                "The batch could not be written because it violates a database constraint or conversion rule.", ex);
        }
    }

    private static bool IsDataError(SqlException ex) =>
        ex.Errors.Cast<SqlError>().Any(e => DataErrorNumbers.Contains(e.Number));

    private static DataTable BuildTable(IReadOnlyList<CasinoWagerEvent> batch)
    {
        var table = new DataTable();
        table.Columns.Add("WagerId", typeof(Guid));
        table.Columns.Add("ProviderName", typeof(string));
        table.Columns.Add("GameName", typeof(string));
        table.Columns.Add("Theme", typeof(string));
        table.Columns.Add("AccountId", typeof(Guid));
        table.Columns.Add("Username", typeof(string));
        table.Columns.Add("TransactionId", typeof(Guid));
        table.Columns.Add("BrandId", typeof(Guid));
        table.Columns.Add("ExternalReferenceId", typeof(Guid));
        table.Columns.Add("TransactionTypeId", typeof(Guid));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("CreatedDateTimeUtc", typeof(DateTime));
        table.Columns.Add("NumberOfBets", typeof(int));
        table.Columns.Add("CountryCode", typeof(string));
        table.Columns.Add("DurationMs", typeof(long));

        foreach (var wager in batch)
        {
            table.Rows.Add(
                wager.WagerId,
                wager.Provider,
                wager.GameName,
                wager.Theme,
                wager.AccountId,
                wager.Username,
                wager.TransactionId,
                wager.BrandId,
                wager.ExternalReferenceId,
                wager.TransactionTypeId,
                wager.Amount,
                wager.CreatedDateTime.UtcDateTime,
                wager.NumberOfBets,
                wager.CountryCode,
                wager.Duration);
        }

        return table;
    }
}