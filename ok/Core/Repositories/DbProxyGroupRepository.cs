using Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Repositories;

/// <summary>
/// Stores proxy groups to a database.
/// </summary>
public class DbProxyGroupRepository : DbRepository<ProxyGroupEntity>, IProxyGroupRepository
{
    public DbProxyGroupRepository(ApplicationDbContext context)
        : base(context)
    {

    }

    /// <inheritdoc/>
    public async override Task<ProxyGroupEntity> GetAsync(int id, CancellationToken cancellationToken = default)
        => await GetAll().Include(w => w.Owner)
        .FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
        .ConfigureAwait(false);
}
