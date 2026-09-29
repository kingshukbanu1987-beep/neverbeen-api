using Microsoft.EntityFrameworkCore;
using NeverBeen.API.Entities;

namespace NeverBeen.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // --- Members, auth, settings, lookups ---------------------------------
    public DbSet<UserProfile> Users => Set<UserProfile>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<LoginDevice> LoginDevices => Set<LoginDevice>();
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();

    // --- Gallery ----------------------------------------------------------
    public DbSet<GalleryAlbum> GalleryAlbums => Set<GalleryAlbum>();
    public DbSet<GalleryPhoto> GalleryPhotos => Set<GalleryPhoto>();

    // --- Message Book -----------------------------------------------------
    public DbSet<CommunityComment> CommunityComments => Set<CommunityComment>();
    public DbSet<CommunityCommentTag> CommunityCommentTags => Set<CommunityCommentTag>();
    public DbSet<CommentReaction> CommentReactions => Set<CommentReaction>();

    // --- Journey feed -----------------------------------------------------
    public DbSet<JourneyPost> JourneyPosts => Set<JourneyPost>();
    public DbSet<JourneyPostAudienceEntry> JourneyPostAudienceEntries => Set<JourneyPostAudienceEntry>();
    public DbSet<JourneyPostTag> JourneyPostTags => Set<JourneyPostTag>();
    public DbSet<JourneyPostReaction> JourneyPostReactions => Set<JourneyPostReaction>();
    public DbSet<JourneyComment> JourneyComments => Set<JourneyComment>();
    public DbSet<JourneyCommentReaction> JourneyCommentReactions => Set<JourneyCommentReaction>();

    // --- Companions, follows ---------------------------------------------
    public DbSet<Companionship> Companionships => Set<Companionship>();
    public DbSet<Follow> Follows => Set<Follow>();

    // --- Circles ----------------------------------------------------------
    public DbSet<Circle> Circles => Set<Circle>();
    public DbSet<CircleMember> CircleMembers => Set<CircleMember>();
    public DbSet<CircleMessage> CircleMessages => Set<CircleMessage>();

    // --- Messenger --------------------------------------------------------
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // --- Notifications & moderation --------------------------------------
    public DbSet<CommunityNotification> Notifications => Set<CommunityNotification>();
    public DbSet<AbuseReport> AbuseReports => Set<AbuseReport>();
    public DbSet<BlockedUser> BlockedUsers => Set<BlockedUser>();
    public DbSet<HiddenPost> HiddenPosts => Set<HiddenPost>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasIndex(u => u.Email).IsUnique();
            entity.HasIndex(u => u.UniqueId).IsUnique();

            entity.HasOne(u => u.Country)
                .WithMany()
                .HasForeignKey(u => u.CountryId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(u => u.City)
                .WithMany()
                .HasForeignKey(u => u.CityId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(u => u.Settings)
                .WithOne(s => s.User)
                .HasForeignKey<UserSettings>(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalIdentity>(entity =>
        {
            entity.HasIndex(i => new { i.Provider, i.ProviderKey }).IsUnique();

            entity.HasOne(i => i.User)
                .WithMany(u => u.Identities)
                .HasForeignKey(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasIndex(s => s.UserId).IsUnique();
        });

        modelBuilder.Entity<LoginDevice>(entity =>
        {
            entity.HasIndex(d => d.UserId);
        });

        modelBuilder.Entity<Country>(entity =>
        {
            entity.HasIndex(c => c.IsoCode2).IsUnique();
        });

        modelBuilder.Entity<City>(entity =>
        {
            entity.HasIndex(c => new { c.CountryId, c.Name }).IsUnique();

            entity.HasOne(c => c.Country)
                .WithMany(c => c.Cities)
                .HasForeignKey(c => c.CountryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GalleryAlbum>(entity =>
        {
            entity.HasIndex(a => a.UserId);
        });

        modelBuilder.Entity<GalleryPhoto>(entity =>
        {
            entity.HasIndex(g => g.UserId);
            entity.HasIndex(g => g.AlbumId);

            entity.HasOne(g => g.User)
                .WithMany(u => u.GalleryPhotos)
                .HasForeignKey(g => g.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommunityComment>(entity =>
        {
            entity.HasIndex(c => c.AuthorId);
            entity.HasIndex(c => c.ParentId);

            entity.HasOne(c => c.Author)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);

            // Replies are removed explicitly in the service layer before a top-level
            // comment is deleted, so the self-reference uses Restrict.
            entity.HasOne(c => c.Parent)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommunityCommentTag>(entity =>
        {
            entity.HasIndex(t => new { t.CommentId, t.UserId }).IsUnique();
        });

        modelBuilder.Entity<CommentReaction>(entity =>
        {
            entity.HasIndex(r => new { r.CommentId, r.UserId }).IsUnique();

            entity.HasOne(r => r.Comment)
                .WithMany()
                .HasForeignKey(r => r.CommentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JourneyPost>(entity =>
        {
            entity.HasIndex(p => p.AuthorId);
            entity.HasIndex(p => p.WallOwnerId);
            entity.HasIndex(p => p.OriginalPostId);
        });

        modelBuilder.Entity<JourneyPostAudienceEntry>(entity =>
        {
            entity.HasIndex(a => new { a.PostId, a.UserId, a.Kind }).IsUnique();
        });

        modelBuilder.Entity<JourneyPostTag>(entity =>
        {
            entity.HasIndex(t => new { t.PostId, t.UserId }).IsUnique();
        });

        modelBuilder.Entity<JourneyPostReaction>(entity =>
        {
            entity.HasIndex(r => new { r.PostId, r.UserId }).IsUnique();
        });

        modelBuilder.Entity<JourneyComment>(entity =>
        {
            entity.HasIndex(c => c.PostId);
            entity.HasIndex(c => c.AuthorId);
            entity.HasIndex(c => c.ParentId);
        });

        modelBuilder.Entity<JourneyCommentReaction>(entity =>
        {
            entity.HasIndex(r => new { r.CommentId, r.UserId }).IsUnique();
        });

        modelBuilder.Entity<Companionship>(entity =>
        {
            entity.HasIndex(c => new { c.UserId, c.CompanionId }).IsUnique();
            entity.HasIndex(c => c.CompanionId);
            entity.HasIndex(c => c.Status);
        });

        modelBuilder.Entity<Follow>(entity =>
        {
            entity.HasIndex(f => new { f.FollowerId, f.FolloweeId }).IsUnique();
            entity.HasIndex(f => f.FolloweeId);
        });

        modelBuilder.Entity<Circle>(entity =>
        {
            entity.HasIndex(c => c.OwnerId);
        });

        modelBuilder.Entity<CircleMember>(entity =>
        {
            entity.HasIndex(m => new { m.CircleId, m.UserId }).IsUnique();
            entity.HasIndex(m => m.UserId);
        });

        modelBuilder.Entity<CircleMessage>(entity =>
        {
            entity.HasIndex(m => new { m.CircleId, m.SentAtUtc });
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasIndex(c => c.CircleId);
        });

        modelBuilder.Entity<ConversationParticipant>(entity =>
        {
            entity.HasIndex(p => new { p.ConversationId, p.UserId }).IsUnique();
            entity.HasIndex(p => p.UserId);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(m => new { m.ConversationId, m.SentAtUtc });
            entity.HasIndex(m => m.SenderId);
        });

        modelBuilder.Entity<CommunityNotification>(entity =>
        {
            entity.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAtUtc });
        });

        modelBuilder.Entity<AbuseReport>(entity =>
        {
            entity.HasIndex(r => new { r.Status, r.CreatedAtUtc });
        });

        modelBuilder.Entity<BlockedUser>(entity =>
        {
            entity.HasIndex(b => new { b.UserId, b.BlockedUserId }).IsUnique();
        });

        modelBuilder.Entity<HiddenPost>(entity =>
        {
            entity.HasIndex(h => new { h.UserId, h.PostId }).IsUnique();
        });
    }
}
