using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;
using System.Text;

namespace Wavelength.Web.Data
{
    /// <summary>Entity Framework 6 context for the 19 tables (connection string "WavelengthDb" in Web.config).</summary>
    public class AppDbContext : DbContext
    {
        public AppDbContext() : base(DefaultConnection()) { }
        public AppDbContext(string nameOrConnectionString) : base(nameOrConnectionString) { }

        // Users & Identity
        public DbSet<User> Users { get; set; }
        public DbSet<ArtistProfile> ArtistProfiles { get; set; }
        public DbSet<Follow> Follows { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        // Music Catalog
        public DbSet<Album> Albums { get; set; }
        public DbSet<Track> Tracks { get; set; }
        public DbSet<Genre> Genres { get; set; }
        public DbSet<TrackGenre> TrackGenres { get; set; }
        // Library & Engagement
        public DbSet<Playlist> Playlists { get; set; }
        public DbSet<PlaylistTrack> PlaylistTracks { get; set; }
        public DbSet<TrackLike> TrackLikes { get; set; }
        public DbSet<SavedAlbum> SavedAlbums { get; set; }
        public DbSet<SavedPlaylist> SavedPlaylists { get; set; }
        public DbSet<ListeningHistory> ListeningHistory { get; set; }
        public DbSet<ShareLink> ShareLinks { get; set; }
        public DbSet<Comment> Comments { get; set; }
        // Administration & Contact
        public DbSet<Report> Reports { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<ContactMessage> ContactMessages { get; set; }

        /// <summary>
        /// "WavelengthDb" from Web.config (LocalDB). The WAVELENGTH_DB environment variable overrides it,
        /// which the Docker setup (docker-compose.yml) uses to point the app at a SQL Server container.
        /// </summary>
        public static string DefaultConnection()
        {
            var env = System.Environment.GetEnvironmentVariable("WAVELENGTH_DB");
            return string.IsNullOrWhiteSpace(env) ? "name=WavelengthDb" : env;
        }

        public static string Snake(string name)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        protected override void OnModelCreating(DbModelBuilder b)
        {
            // SQL Server does not allow multiple cascade paths; deletes are handled in CleanupService.
            b.Conventions.Remove<OneToManyCascadeDeleteConvention>();
            b.Conventions.Remove<ManyToManyCascadeDeleteConvention>();
            b.Conventions.Remove<PluralizingTableNameConvention>();

            // snake_case column names, like the report (password_hash, created_at ...)
            b.Properties().Configure(p => p.HasColumnName(Snake(p.ClrPropertyInfo.Name)));
            b.Properties<string>().Configure(p => p.IsUnicode(true));

            // users 1 - 0..1 artist_profiles (unique index on user_id)
            b.Entity<ArtistProfile>().HasRequired(a => a.User).WithMany(u => u.ArtistProfiles).HasForeignKey(a => a.UserId);

            // follows: users <-> users
            b.Entity<Follow>().HasKey(f => new { f.FollowerId, f.FollowedId });
            b.Entity<Follow>().HasRequired(f => f.Follower).WithMany(u => u.Following).HasForeignKey(f => f.FollowerId);
            b.Entity<Follow>().HasRequired(f => f.Followed).WithMany(u => u.Followers).HasForeignKey(f => f.FollowedId);

            b.Entity<Notification>().HasRequired(n => n.User).WithMany().HasForeignKey(n => n.UserId);

            b.Entity<Album>().HasRequired(a => a.Artist).WithMany(a => a.Albums).HasForeignKey(a => a.ArtistId);

            b.Entity<Track>().HasRequired(t => t.Artist).WithMany(a => a.Tracks).HasForeignKey(t => t.ArtistId);
            b.Entity<Track>().HasOptional(t => t.Album).WithMany(a => a.Tracks).HasForeignKey(t => t.AlbumId);

            // track_genres: tracks <-> genres
            b.Entity<TrackGenre>().HasKey(x => new { x.TrackId, x.GenreId });
            b.Entity<TrackGenre>().HasRequired(x => x.Track).WithMany(t => t.TrackGenres).HasForeignKey(x => x.TrackId);
            b.Entity<TrackGenre>().HasRequired(x => x.Genre).WithMany(g => g.TrackGenres).HasForeignKey(x => x.GenreId);

            b.Entity<Playlist>().HasRequired(p => p.Owner).WithMany(u => u.Playlists).HasForeignKey(p => p.OwnerId);

            // playlist_tracks: playlists <-> tracks
            b.Entity<PlaylistTrack>().HasKey(x => new { x.PlaylistId, x.TrackId });
            b.Entity<PlaylistTrack>().HasRequired(x => x.Playlist).WithMany(p => p.PlaylistTracks).HasForeignKey(x => x.PlaylistId);
            b.Entity<PlaylistTrack>().HasRequired(x => x.Track).WithMany().HasForeignKey(x => x.TrackId);

            // track_likes: users <-> tracks
            b.Entity<TrackLike>().HasKey(x => new { x.UserId, x.TrackId });
            b.Entity<TrackLike>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            b.Entity<TrackLike>().HasRequired(x => x.Track).WithMany(t => t.Likes).HasForeignKey(x => x.TrackId);

            b.Entity<SavedAlbum>().HasKey(x => new { x.UserId, x.AlbumId });
            b.Entity<SavedAlbum>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            b.Entity<SavedAlbum>().HasRequired(x => x.Album).WithMany().HasForeignKey(x => x.AlbumId);

            b.Entity<SavedPlaylist>().HasKey(x => new { x.UserId, x.PlaylistId });
            b.Entity<SavedPlaylist>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            b.Entity<SavedPlaylist>().HasRequired(x => x.Playlist).WithMany().HasForeignKey(x => x.PlaylistId);

            b.Entity<ListeningHistory>().HasRequired(x => x.User).WithMany().HasForeignKey(x => x.UserId);
            b.Entity<ListeningHistory>().HasRequired(x => x.Track).WithMany().HasForeignKey(x => x.TrackId);

            b.Entity<ShareLink>().HasRequired(x => x.Track).WithMany().HasForeignKey(x => x.TrackId);
            b.Entity<ShareLink>().HasRequired(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy);

            // comments: self reference for replies
            b.Entity<Comment>().HasRequired(c => c.User).WithMany().HasForeignKey(c => c.UserId);
            b.Entity<Comment>().HasRequired(c => c.Track).WithMany(t => t.Comments).HasForeignKey(c => c.TrackId);
            b.Entity<Comment>().HasOptional(c => c.Parent).WithMany(c => c.Replies).HasForeignKey(c => c.ParentId);

            // reports: three optional targets (a CHECK constraint makes sure exactly one is filled)
            b.Entity<Report>().HasRequired(r => r.Reporter).WithMany().HasForeignKey(r => r.ReporterId);
            b.Entity<Report>().HasOptional(r => r.Track).WithMany().HasForeignKey(r => r.TrackId);
            b.Entity<Report>().HasOptional(r => r.Comment).WithMany().HasForeignKey(r => r.CommentId);
            b.Entity<Report>().HasOptional(r => r.ReportedUser).WithMany().HasForeignKey(r => r.ReportedUserId);
            b.Entity<Report>().HasOptional(r => r.Handler).WithMany().HasForeignKey(r => r.HandledBy);

            b.Entity<AuditLog>().HasRequired(a => a.Admin).WithMany().HasForeignKey(a => a.AdminId);
        }
    }
}
