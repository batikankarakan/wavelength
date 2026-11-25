using Microsoft.EntityFrameworkCore;

namespace Wavelength.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Users & Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<ArtistProfile> ArtistProfiles => Set<ArtistProfile>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<Notification> Notifications => Set<Notification>();
    // Music Catalog
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<TrackGenre> TrackGenres => Set<TrackGenre>();
    // Library & Engagement
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistTrack> PlaylistTracks => Set<PlaylistTrack>();
    public DbSet<TrackLike> TrackLikes => Set<TrackLike>();
    public DbSet<SavedAlbum> SavedAlbums => Set<SavedAlbum>();
    public DbSet<SavedPlaylist> SavedPlaylists => Set<SavedPlaylist>();
    public DbSet<ListeningHistory> ListeningHistory => Set<ListeningHistory>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<Comment> Comments => Set<Comment>();
    // Administration & Contact
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // users
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_users_role", "role IN ('listener','artist','moderator','admin')"));
        });

        // artist_profiles: one-to-one with users
        b.Entity<ArtistProfile>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithOne(u => u.ArtistProfile).HasForeignKey<ArtistProfile>(x => x.UserId);
        });

        // follows: users <-> users
        b.Entity<Follow>(e =>
        {
            e.HasKey(x => new { x.FollowerId, x.FollowedId });
            e.HasOne(x => x.Follower).WithMany(u => u.Following).HasForeignKey(x => x.FollowerId);
            e.HasOne(x => x.Followed).WithMany(u => u.Followers).HasForeignKey(x => x.FollowedId);
        });

        b.Entity<Notification>().HasIndex(x => new { x.UserId, x.IsRead });

        // albums
        b.Entity<Album>(e =>
        {
            e.HasOne(x => x.Artist).WithMany(a => a.Albums).HasForeignKey(x => x.ArtistId);
            e.ToTable(t => t.HasCheckConstraint("ck_albums_type", "type IN ('single','ep','album')"));
        });

        // tracks
        b.Entity<Track>(e =>
        {
            e.HasOne(x => x.Artist).WithMany(a => a.Tracks).HasForeignKey(x => x.ArtistId);
            e.HasOne(x => x.Album).WithMany(a => a.Tracks).HasForeignKey(x => x.AlbumId);
            e.HasIndex(x => x.Status);
            e.ToTable(t => t.HasCheckConstraint("ck_tracks_status", "status IN ('draft','pending','published','rejected','hidden')"));
        });

        // genres
        b.Entity<Genre>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
        });

        // track_genres: tracks <-> genres
        b.Entity<TrackGenre>(e =>
        {
            e.HasKey(x => new { x.TrackId, x.GenreId });
            e.HasOne(x => x.Track).WithMany(t => t.TrackGenres).HasForeignKey(x => x.TrackId);
            e.HasOne(x => x.Genre).WithMany(g => g.TrackGenres).HasForeignKey(x => x.GenreId);
        });

        b.Entity<Playlist>().HasOne(x => x.Owner).WithMany(u => u.Playlists).HasForeignKey(x => x.OwnerId);

        // playlist_tracks: playlists <-> tracks
        b.Entity<PlaylistTrack>(e =>
        {
            e.HasKey(x => new { x.PlaylistId, x.TrackId });
            e.HasOne(x => x.Playlist).WithMany(p => p.PlaylistTracks).HasForeignKey(x => x.PlaylistId);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId);
        });

        // track_likes: users <-> tracks
        b.Entity<TrackLike>(e =>
        {
            e.HasKey(x => new { x.UserId, x.TrackId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Track).WithMany(t => t.Likes).HasForeignKey(x => x.TrackId);
        });

        b.Entity<SavedAlbum>(e =>
        {
            e.HasKey(x => new { x.UserId, x.AlbumId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Album).WithMany().HasForeignKey(x => x.AlbumId);
        });

        b.Entity<SavedPlaylist>(e =>
        {
            e.HasKey(x => new { x.UserId, x.PlaylistId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Playlist).WithMany().HasForeignKey(x => x.PlaylistId);
        });

        b.Entity<ListeningHistory>(e =>
        {
            e.ToTable("listening_history");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId);
            e.HasIndex(x => x.PlayedAt);
        });

        b.Entity<ShareLink>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);
        });

        // comments: self reference for replies
        b.Entity<Comment>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Track).WithMany(t => t.Comments).HasForeignKey(x => x.TrackId);
            e.HasOne(x => x.Parent).WithMany(c => c.Replies).HasForeignKey(x => x.ParentId);
        });

        // reports: exactly one target is filled
        b.Entity<Report>(e =>
        {
            e.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterId);
            e.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId);
            e.HasOne(x => x.Comment).WithMany().HasForeignKey(x => x.CommentId);
            e.HasOne(x => x.ReportedUser).WithMany().HasForeignKey(x => x.ReportedUserId);
            e.HasOne(x => x.Handler).WithMany().HasForeignKey(x => x.HandledBy);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_reports_status", "status IN ('open','resolved','dismissed')");
                t.HasCheckConstraint("ck_reports_one_target",
                    "(CASE WHEN track_id IS NULL THEN 0 ELSE 1 END + CASE WHEN comment_id IS NULL THEN 0 ELSE 1 END + CASE WHEN reported_user_id IS NULL THEN 0 ELSE 1 END) = 1");
            });
        });

        b.Entity<AuditLog>().HasOne(x => x.Admin).WithMany().HasForeignKey(x => x.AdminId);

        b.Entity<ContactMessage>().ToTable(t => t.HasCheckConstraint("ck_contact_status", "status IN ('new','read','replied')"));

        // SQL Server does not allow multiple cascade paths; deletes are handled explicitly in code.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
