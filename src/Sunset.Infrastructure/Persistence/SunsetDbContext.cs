using Microsoft.EntityFrameworkCore;
using Sunset.Domain.Entities;

namespace Sunset.Infrastructure.Persistence;

public class SunsetDbContext(DbContextOptions<SunsetDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Like> Likes => Set<Like>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Rating> Ratings => Set<Rating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SunsetDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
