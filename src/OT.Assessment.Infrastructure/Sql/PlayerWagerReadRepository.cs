using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using OT.Assessment.Core.Interfaces;
using OT.Assessment.Core.Models;

namespace OT.Assessment.Infrastructure.Sql;

/// <summary>Read-only queries backing the player GET endpoints, executed against SQL Server via Dapper.</summary>
public sealed class PlayerWagerReadRepository(string connectionString) : IPlayerWagerReadRepository
{
    public async Task<PagedResult<PlayerCasinoWager>> GetWagerPageAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);

        var command = new CommandDefinition(
            "dbo.usp_CasinoWager_GetPageByAccountId",
            new { AccountId = accountId, PageNumber = page, PageSize = pageSize },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        await connection.OpenAsync(cancellationToken);
        using var multi = await connection.QueryMultipleAsync(command);

        var total = await multi.ReadSingleAsync<int>();
        var rows = (await multi.ReadAsync<PlayerCasinoWagerRow>())
            .Select(r => new PlayerCasinoWager(r.WagerId, r.Game, r.Provider, r.Amount, DateTime.SpecifyKind(r.CreatedDate, DateTimeKind.Utc)))
            .ToList();

        return new PagedResult<PlayerCasinoWager>(rows, page, pageSize, total);
    }

    public async Task<IReadOnlyList<TopSpender>> GetTopSpendersAsync(int count, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);

        var command = new CommandDefinition(
            "dbo.usp_Player_GetTopSpenders",
            new { Count = count },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<TopSpender>(command);
        return rows.ToList();
    }

    private sealed record PlayerCasinoWagerRow(Guid WagerId, string Game, string Provider, decimal Amount, DateTime CreatedDate);
}