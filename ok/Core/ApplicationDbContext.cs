using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; // Add this for logging

namespace Core;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<ProxyEntity> Proxies { get; set; }
    public DbSet<ProxyGroupEntity> ProxyGroups { get; set; }
    public DbSet<WordlistEntity> Wordlists { get; set; }
    public DbSet<JobEntity> Jobs { get; set; }
    public DbSet<RecordEntity> Records { get; set; }
    public DbSet<HitEntity> Hits { get; set; }
    public DbSet<GuestEntity> Guests { get; set; }

    // ADD THIS METHOD
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // This will show you the exact ID and query causing the tracking conflict
        optionsBuilder.EnableSensitiveDataLogging();

        // OPTIONAL: If you want to completely disable tracking globally (be careful, this might break Update operations)
        // optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProxyGroupEntity>()
            .HasMany(g => g.Proxies)
            .WithOne(u => u.Group)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ProxyGroupEntity>()
            .HasOne(g => g.Owner)
            .WithMany(u => u.ProxyGroups)
            .OnDelete(DeleteBehavior.SetNull);

        base.OnModelCreating(modelBuilder);
    }
}
