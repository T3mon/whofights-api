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
    public DbSet<RankingList> RankingLists => Set<RankingList>();
    public DbSet<RankingEntry> RankingEntries => Set<RankingEntry>();

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

        builder.Entity<RankingList>(e =>
        {
            e.HasMany(l => l.Entries)
                .WithOne(en => en.RankingList)
                .HasForeignKey(en => en.RankingListId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RankingEntry>(e =>
        {
            // Stored as text so the table reads like the source and reordering
            // the enum can never silently remap rows.
            e.Property(en => en.Position).HasConversion<string>();

            // A fighter row going away (it never does today) just unlinks the
            // ranking entry; the ranking itself is still true.
            e.HasOne(en => en.Fighter)
                .WithMany()
                .HasForeignKey(en => en.FighterId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
