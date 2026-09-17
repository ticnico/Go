using Core.Entities;

namespace Core.Repositories;

/// <summary>
/// Stores guests to a database.
/// </summary>
public class DbGuestRepository : DbRepository<GuestEntity>, IGuestRepository
{
    public DbGuestRepository(ApplicationDbContext context)
        : base(context)
    {

    }
}
