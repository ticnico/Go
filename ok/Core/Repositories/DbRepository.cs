using Core.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Repositories;

/// <summary>
/// Stores data to a database.
/// </summary>
/// <typeparam name="T">The type of data to store</typeparam>
public class DbRepository<T> : IRepository<T> where T : Entity
{
    protected readonly ApplicationDbContext context;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public DbRepository(ApplicationDbContext context)
    {
        this.context = context;
    }

    /// <inheritdoc/>
    public async virtual Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            context.Add(entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async virtual Task AddAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            context.AddRange(entities);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async virtual Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var entry = context.Entry(entity);
            var keyProperties = entry.Metadata.FindPrimaryKey().Properties;

            // Check if the DbContext is already tracking an entity with this exact same ID
            var trackedEntity = context.ChangeTracker.Entries<T>()
                .FirstOrDefault(e => keyProperties.All(p =>
                    e.Property(p.Name).CurrentValue.Equals(entry.Property(p.Name).CurrentValue)))?.Entity;

            if (trackedEntity != null)
            {
                // If it's already tracked, mark the TRACKED instance for deletion
                context.Remove(trackedEntity);
            }
            else
            {
                // If it's not tracked, attach it and mark for deletion
                context.Entry(entity).State = EntityState.Deleted;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async virtual Task DeleteAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var entity in entities)
            {
                var entry = context.Entry(entity);
                var keyProperties = entry.Metadata.FindPrimaryKey().Properties;

                var trackedEntity = context.ChangeTracker.Entries<T>()
                    .FirstOrDefault(e => keyProperties.All(p =>
                        e.Property(p.Name).CurrentValue.Equals(entry.Property(p.Name).CurrentValue)))?.Entity;

                if (trackedEntity != null)
                {
                    context.Remove(trackedEntity);
                }
                else
                {
                    context.Entry(entity).State = EntityState.Deleted;
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async virtual Task<T> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await GetAll()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public virtual IQueryable<T> GetAll()
        => context.Set<T>();

    /// <inheritdoc/>
    public async virtual Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var entry = context.Entry(entity);
            var keyProperties = entry.Metadata.FindPrimaryKey().Properties;

            var trackedEntity = context.ChangeTracker.Entries<T>()
                .FirstOrDefault(e => keyProperties.All(p =>
                    e.Property(p.Name).CurrentValue.Equals(entry.Property(p.Name).CurrentValue)))?.Entity;

            if (trackedEntity != null)
            {
                // If it's already tracked, copy the new values into the TRACKED instance
                context.Entry(trackedEntity).CurrentValues.SetValues(entity);
            }
            else
            {
                // If it's not tracked, attach it and mark as modified
                context.Entry(entity).State = EntityState.Modified;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async virtual Task UpdateAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var entity in entities)
            {
                var entry = context.Entry(entity);
                var keyProperties = entry.Metadata.FindPrimaryKey().Properties;

                var trackedEntity = context.ChangeTracker.Entries<T>()
                    .FirstOrDefault(e => keyProperties.All(p =>
                        e.Property(p.Name).CurrentValue.Equals(entry.Property(p.Name).CurrentValue)))?.Entity;

                if (trackedEntity != null)
                {
                    context.Entry(trackedEntity).CurrentValues.SetValues(entity);
                }
                else
                {
                    context.Entry(entity).State = EntityState.Modified;
                }
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public void Attach<TEntity>(TEntity entity) where TEntity : Entity => context.Attach(entity);
}
