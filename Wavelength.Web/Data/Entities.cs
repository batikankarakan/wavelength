using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Wavelength.Web.Data
{
    // The 19 tables of the analysis report (section 2). Table names are set with [Table];
    // column names are converted to snake_case in AppDbContext (PasswordHash -> password_hash).

    public static class AppRoles
    {
        public const string Listener = "listener";
        public const string Artist = "artist";
        public const string Moderator = "moderator";
        public const string Admin = "admin";
        public const string Staff = Moderator + "," + Admin; // for [Authorize(Roles = AppRoles.Staff)]
        public static readonly string[] All = { Listener, Artist, Moderator, Admin };
    }

    public static class TrackStatus
    {
        public const string Draft = "draft";
        public const string Pending = "pending";
        public const string Published = "published";
        public const string Rejected = "rejected";
        public const string Hidden = "hidden";
        public static readonly string[] All = { Draft, Pending, Published, Rejected, Hidden };
    }

    // ---------- 2.2 Users & Identity ----------

    [Table("users")]
    public class User
    {
        public User()
        {
            Role = AppRoles.Listener;
            IsActive = true;
            CreatedAt = DateTime.UtcNow;
            ArtistProfiles = new List<ArtistProfile>();
            Playlists = new List<Playlist>();
            Following = new List<Follow>();
            Followers = new List<Follow>();
        }

        public int Id { get; set; }
        [Required, MaxLength(30), Index(IsUnique = true)] public string Username { get; set; }
        [Required, MaxLength(120), Index(IsUnique = true)] public string Email { get; set; }
        [Required, MaxLength(255)] public string PasswordHash { get; set; }
        [Required, MaxLength(60)] public string DisplayName { get; set; }
        [MaxLength(255)] public string AvatarUrl { get; set; }
        [MaxLength(300)] public string Bio { get; set; }
        [Required, MaxLength(20)] public string Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>At most one row (unique index on artist_profiles.user_id).</summary>
        public virtual ICollection<ArtistProfile> ArtistProfiles { get; set; }
        [NotMapped] public ArtistProfile ArtistProfile { get { return ArtistProfiles.FirstOrDefault(); } }
        public virtual ICollection<Playlist> Playlists { get; set; }
        public virtual ICollection<Follow> Following { get; set; }
        public virtual ICollection<Follow> Followers { get; set; }
    }

    [Table("artist_profiles")]
    public class ArtistProfile
    {
        public ArtistProfile()
        {
            Albums = new List<Album>();
            Tracks = new List<Track>();
        }

        public int Id { get; set; }
        [Index(IsUnique = true)] public int UserId { get; set; }
        [Required, MaxLength(80)] public string StageName { get; set; }
        public bool Verified { get; set; }
        [MaxLength(255)] public string BannerUrl { get; set; }
        [MaxLength(60)] public string Country { get; set; }
        [MaxLength(150)] public string Website { get; set; }

        public virtual User User { get; set; }
        public virtual ICollection<Album> Albums { get; set; }
        public virtual ICollection<Track> Tracks { get; set; }
    }

    [Table("follows")]
    public class Follow
    {
        public Follow() { CreatedAt = DateTime.UtcNow; }

        public int FollowerId { get; set; }
        public int FollowedId { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual User Follower { get; set; }
        public virtual User Followed { get; set; }
    }

    [Table("notifications")]
    public class Notification
    {
        public Notification() { Payload = "{}"; CreatedAt = DateTime.UtcNow; }

        public int Id { get; set; }
        [Index("ix_notifications_user_read", 1)] public int UserId { get; set; }
        [Required, MaxLength(30)] public string Type { get; set; }
        [Required, MaxLength(500)] public string Payload { get; set; }
        [Index("ix_notifications_user_read", 2)] public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual User User { get; set; }
    }

    // ---------- 2.3 Music Catalog ----------

    [Table("albums")]
    public class Album
    {
        public Album() { Type = "album"; Tracks = new List<Track>(); }

        public int Id { get; set; }
        public int ArtistId { get; set; }
        [Required, MaxLength(100)] public string Title { get; set; }
        [MaxLength(255)] public string CoverUrl { get; set; }
        [Column(TypeName = "date")] public DateTime ReleaseDate { get; set; }
        [Required, MaxLength(10)] public string Type { get; set; } // single, ep or album

        public virtual ArtistProfile Artist { get; set; }
        public virtual ICollection<Track> Tracks { get; set; }
    }

    [Table("tracks")]
    public class Track
    {
        public Track()
        {
            Status = TrackStatus.Draft;
            CreatedAt = DateTime.UtcNow;
            TrackGenres = new List<TrackGenre>();
            Likes = new List<TrackLike>();
            Comments = new List<Comment>();
        }

        public int Id { get; set; }
        public int ArtistId { get; set; }
        public int? AlbumId { get; set; }
        [Required, MaxLength(120)] public string Title { get; set; }
        [Required, MaxLength(255)] public string AudioUrl { get; set; }
        [MaxLength(255)] public string CoverUrl { get; set; }
        public int DurationSec { get; set; }
        public string Lyrics { get; set; }
        [Required, MaxLength(12), Index] public string Status { get; set; }
        public int PlaysCount { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual ArtistProfile Artist { get; set; }
        public virtual Album Album { get; set; }
        public virtual ICollection<TrackGenre> TrackGenres { get; set; }
        public virtual ICollection<TrackLike> Likes { get; set; }
        public virtual ICollection<Comment> Comments { get; set; }
    }

    [Table("genres")]
    public class Genre
    {
        public Genre() { Color = "#FFE500"; TrackGenres = new List<TrackGenre>(); }

        public int Id { get; set; }
        [Required, MaxLength(40), Index(IsUnique = true)] public string Name { get; set; }
        [Required, MaxLength(40), Index(IsUnique = true)] public string Slug { get; set; }
        [Required, MaxLength(7)] public string Color { get; set; }

        public virtual ICollection<TrackGenre> TrackGenres { get; set; }
    }

    [Table("track_genres")]
    public class TrackGenre
    {
        public int TrackId { get; set; }
        public int GenreId { get; set; }

        public virtual Track Track { get; set; }
        public virtual Genre Genre { get; set; }
    }

    // ---------- 2.4 Library & Engagement ----------

    [Table("playlists")]
    public class Playlist
    {
        public Playlist() { IsPublic = true; CreatedAt = DateTime.UtcNow; PlaylistTracks = new List<PlaylistTrack>(); }

        public int Id { get; set; }
        public int OwnerId { get; set; }
        [Required, MaxLength(100)] public string Title { get; set; }
        [MaxLength(300)] public string Description { get; set; }
        [MaxLength(255)] public string CoverUrl { get; set; }
        public bool IsPublic { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual User Owner { get; set; }
        public virtual ICollection<PlaylistTrack> PlaylistTracks { get; set; }
    }

    [Table("playlist_tracks")]
    public class PlaylistTrack
    {
        public PlaylistTrack() { AddedAt = DateTime.UtcNow; }

        public int PlaylistId { get; set; }
        public int TrackId { get; set; }
        public int Position { get; set; }
        public DateTime AddedAt { get; set; }

        public virtual Playlist Playlist { get; set; }
        public virtual Track Track { get; set; }
    }

    [Table("track_likes")]
    public class TrackLike
    {
        public TrackLike() { CreatedAt = DateTime.UtcNow; }

        public int UserId { get; set; }
        public int TrackId { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual User User { get; set; }
        public virtual Track Track { get; set; }
    }

    [Table("saved_albums")]
    public class SavedAlbum
    {
        public SavedAlbum() { SavedAt = DateTime.UtcNow; }

        public int UserId { get; set; }
        public int AlbumId { get; set; }
        public DateTime SavedAt { get; set; }

        public virtual User User { get; set; }
        public virtual Album Album { get; set; }
    }

    [Table("saved_playlists")]
    public class SavedPlaylist
    {
        public SavedPlaylist() { SavedAt = DateTime.UtcNow; }

        public int UserId { get; set; }
        public int PlaylistId { get; set; }
        public DateTime SavedAt { get; set; }

        public virtual User User { get; set; }
        public virtual Playlist Playlist { get; set; }
    }

    [Table("listening_history")]
    public class ListeningHistory
    {
        public ListeningHistory() { PlayedAt = DateTime.UtcNow; }

        public int Id { get; set; }
        public int UserId { get; set; }
        public int TrackId { get; set; }
        [Index] public DateTime PlayedAt { get; set; }
        public int SecondsListened { get; set; }

        public virtual User User { get; set; }
        public virtual Track Track { get; set; }
    }

    [Table("share_links")]
    public class ShareLink
    {
        public ShareLink() { CreatedAt = DateTime.UtcNow; }

        public int Id { get; set; }
        public int TrackId { get; set; }
        public int CreatedBy { get; set; }
        [Required, MaxLength(32), Index(IsUnique = true)] public string Token { get; set; }
        public int ClickCount { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual Track Track { get; set; }
        public virtual User Creator { get; set; }
    }

    [Table("comments")]
    public class Comment
    {
        public Comment() { CreatedAt = DateTime.UtcNow; Replies = new List<Comment>(); }

        public int Id { get; set; }
        public int UserId { get; set; }
        public int TrackId { get; set; }
        public int? ParentId { get; set; }
        [Required] public string Body { get; set; }
        public bool IsHidden { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual User User { get; set; }
        public virtual Track Track { get; set; }
        public virtual Comment Parent { get; set; }
        public virtual ICollection<Comment> Replies { get; set; }
    }

    // ---------- 2.5 Administration & Contact ----------

    [Table("reports")]
    public class Report
    {
        public Report() { Status = "open"; }

        public int Id { get; set; }
        public int ReporterId { get; set; }
        public int? TrackId { get; set; }
        public int? CommentId { get; set; }
        public int? ReportedUserId { get; set; }
        [Required, MaxLength(300)] public string Reason { get; set; }
        [Required, MaxLength(12)] public string Status { get; set; } // open, resolved, dismissed
        public int? HandledBy { get; set; }

        public virtual User Reporter { get; set; }
        public virtual Track Track { get; set; }
        public virtual Comment Comment { get; set; }
        public virtual User ReportedUser { get; set; }
        public virtual User Handler { get; set; }
    }

    [Table("audit_logs")]
    public class AuditLog
    {
        public AuditLog() { CreatedAt = DateTime.UtcNow; }

        public int Id { get; set; }
        public int AdminId { get; set; }
        [Required, MaxLength(60)] public string Action { get; set; }
        [Required, MaxLength(120)] public string Target { get; set; }
        public DateTime CreatedAt { get; set; }

        public virtual User Admin { get; set; }
    }

    [Table("contact_messages")]
    public class ContactMessage
    {
        public ContactMessage() { Topic = "general"; Status = "new"; CreatedAt = DateTime.UtcNow; }

        public int Id { get; set; }
        [Required, MaxLength(80)] public string Name { get; set; }
        [Required, MaxLength(120)] public string Email { get; set; }
        [Required, MaxLength(30)] public string Topic { get; set; } // general, feedback, bug, collaboration
        [Required] public string Message { get; set; }
        [Required, MaxLength(10)] public string Status { get; set; } // new, read, replied
        public DateTime CreatedAt { get; set; }
    }
}
