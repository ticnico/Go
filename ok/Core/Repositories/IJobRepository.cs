using Core.Entities;

namespace Core.Repositories;

/// <summary>
/// Stores jobs.
/// </summary>
public interface IJobRepository : IRepository<JobEntity>
{
    /// <summary>
    /// Deletes all jobs from the repository.
    /// </summary>
    void Purge();
}
