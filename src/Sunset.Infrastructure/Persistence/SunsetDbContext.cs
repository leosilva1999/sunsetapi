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
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();
    public DbSet<TermsOfService> TermsOfServiceDocuments => Set<TermsOfService>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SunsetDbContext).Assembly);

        // Hides moderator-soft-deleted content from every existing query (feed, search,
        // GetById, comments, replies, user/location photo listings) without touching any of
        // those repository methods.
        modelBuilder.Entity<Photo>().HasQueryFilter(p => p.DeletedAt == null);
        modelBuilder.Entity<Comment>().HasQueryFilter(c => c.DeletedAt == null);

        base.OnModelCreating(modelBuilder);
    }
}
