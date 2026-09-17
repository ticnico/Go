using Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Repositories;

/// <summary>
/// Stores proxies to a database.
/// </summary>
public class DbProxyRepository : DbRepository<ProxyEntity>, IProxyRepository
{
    public DbProxyRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async override Task UpdateAsync(ProxyEntity entity, CancellationToken cancellationToken = default)
    {
        context.Entry(entity).State = EntityState.Modified;
        await base.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public async override Task UpdateAsync(IEnumerable<ProxyEntity> entities, CancellationToken cancellationToken = default)
    {
        foreach (var entity in entities)
        {
            context.Entry(entity).State = EntityState.Modified;
        }

        await base.UpdateAsync(entities, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int> RemoveDuplicatesAsync(int groupId)
    {
        // ✅ OPTIMIZED: Perform duplicate removal entirely in the database using a single SQL query.
        // This prevents loading 140,000+ entities into C# memory, which was causing the freeze.

        var tableName = context.Model.FindEntityType(typeof(ProxyEntity)).GetTableName() ?? "Proxies";

        // Note: Ensure "GroupId" matches your actual database column name for the foreign key.
        // If it's named differently (e.g., ProxyGroupId), update it in the SQL string below.
        var sql = $@"
            DELETE FROM {tableName}
            WHERE Id IN (
                SELECT Id FROM (
                    SELECT Id, ROW_NUMBER() OVER(
                        PARTITION BY Type, Host, Port, COALESCE(Username, ''), COALESCE(Password, '')
                        ORDER BY Id
                    ) as rn
                    FROM {tableName}
                    WHERE GroupId = {{0}}
                ) t WHERE t.rn > 1
            )";

        var rowsAffected = await context.Database.ExecuteSqlRawAsync(sql, groupId);
        return rowsAffected;
    }

    /// <summary>
    /// ✅ NEW: Ultra-fast bulk delete by IDs.
    /// Executes a SINGLE DELETE statement instead of thousands of individual ones.
    /// </summary>
    public async Task DeleteByIdsAsync(IEnumerable<int> ids)
    {
        var idList = ids.ToList();
        if (!idList.Any()) return;

        var tableName = context.Model.FindEntityType(typeof(ProxyEntity)).GetTableName() ?? "Proxies";
        var idsString = string.Join(",", idList);

        // Executes a single, lightning-fast DELETE statement
        await context.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName} WHERE Id IN ({idsString})");
    }
}
