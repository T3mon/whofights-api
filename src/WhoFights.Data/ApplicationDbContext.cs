using WhoFights.Data.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WhoFights.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<Fighter> Fighters => Set<Fighter>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Bout> Bouts => Set<Bout>();
    public DbSet<UserFollow> UserFollows => Set<UserFollow>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Promotion>(e =>
        {
            e.HasIndex(p => p.Code).IsUnique();
        });

        builder.Entity<Event>(e =>
        {
            e.HasIndex(ev => ev.TapologySlug).IsUnique();
            e.HasIndex(ev => ev.StartsAt);
            e.HasOne(ev => ev.Promotion)
                .WithMany(p => p.Events)
                .HasForeignKey(ev => ev.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Bout>(e =>
        {
            e.HasOne(b => b.Event)
                .WithMany(ev => ev.Bouts)
                .HasForeignKey(b => b.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict, not cascade: a fighter can appear in many bouts and
            // must not be deletable out from under historical bout rows.
            e.HasOne(b => b.FighterA)
                .WithMany()
                .HasForeignKey(b => b.FighterAId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(b => b.FighterB)
                .WithMany()
                .HasForeignKey(b => b.FighterBId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserFollow>(e =>
        {
            e.HasOne(f => f.Promotion)
                .WithMany()
                .HasForeignKey(f => f.PromotionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(f => f.Fighter)
                .WithMany()
                .HasForeignKey(f => f.FighterId)
                .OnDelete(DeleteBehavior.Cascade);

            // No navigation property back to IdentityUser - UserFollow doesn't
            // need to load the user, it just needs deleting a user to clean
            // up their follows instead of leaving orphaned rows behind.
            e.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(f => new { f.UserId, f.PromotionId, f.SubSeries, f.FighterId }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint(
                "CK_UserFollow_SubSeriesNeedsPromotion",
                "\"SubSeries\" IS NULL OR \"PromotionId\" IS NOT NULL"));

            e.ToTable(t => t.HasCheckConstraint(
                "CK_UserFollow_ExactlyOneTarget",
                "(\"PromotionId\" IS NOT NULL) <> (\"FighterId\" IS NOT NULL)"));
        });

        builder.Entity<NotificationPreference>(e =>
        {
            e.HasKey(p => p.UserId);
            e.HasIndex(p => p.UnsubscribeToken).IsUnique();

            // Same reasoning as UserFollow: no navigation back to the user,
            // just cleanup when the account goes.
            e.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
