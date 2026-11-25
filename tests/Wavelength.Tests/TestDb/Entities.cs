using System.ComponentModel.DataAnnotations;

namespace Wavelength.Web.Data;

// The 19 tables of the analysis report (section 2). Table and column names are mapped
// to snake_case by the naming convention in AppDbContext, so `PasswordHash` -> `password_hash`.

public static class Roles
{
    public const string Listener = "listener";
    public const string Artist = "artist";
    public const string Moderator = "moderator";
    public const string Admin = "admin";
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

public class User
{
    public int Id { get; set; }
    [MaxLength(30)] public string Username { get; set; } = "";
    [MaxLength(120)] public string Email { get; set; } = "";
    [MaxLength(255)] public string PasswordHash { get; set; } = "";
    [MaxLength(60)] public string DisplayName { get; set; } = "";
    [MaxLength(255)] public string? AvatarUrl { get; set; }
    [MaxLength(300)] public string? Bio { get; set; }
    [MaxLength(20)] public string Role { get; set; } = Roles.Listener;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ArtistProfile? ArtistProfile { get; set; }
    public List<Playlist> Playlists { get; set; } = new();
    public List<Follow> Following { get; set; } = new();
    public List<Follow> Followers { get; set; } = new();
}

public class ArtistProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(80)] public string StageName { get; set; } = "";
    public bool Verified { get; set; }
    [MaxLength(255)] public string? BannerUrl { get; set; }
    [MaxLength(60)] public string? Country { get; set; }
    [MaxLength(150)] public string? Website { get; set; }

    public User User { get; set; } = null!;
    public List<Album> Albums { get; set; } = new();
    public List<Track> Tracks { get; set; } = new();
}

public class Follow
{
    public int FollowerId { get; set; }
    public int FollowedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Follower { get; set; } = null!;
    public User Followed { get; set; } = null!;
}

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [MaxLength(30)] public string Type { get; set; } = "";
    [MaxLength(500)] public string Payload { get; set; } = "{}";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}

// ---------- 2.3 Music Catalog ----------

public class Album
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    [MaxLength(100)] public string Title { get; set; } = "";
    [MaxLength(255)] public string? CoverUrl { get; set; }
    public DateOnly ReleaseDate { get; set; }
    [MaxLength(10)] public string Type { get; set; } = "album"; // single, ep or album

    public ArtistProfile Artist { get; set; } = null!;
    public List<Track> Tracks { get; set; } = new();
}

public class Track
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public int? AlbumId { get; set; }
    [MaxLength(120)] public string Title { get; set; } = "";
    [MaxLength(255)] public string AudioUrl { get; set; } = "";
    [MaxLength(255)] public string? CoverUrl { get; set; }
    public int DurationSec { get; set; }
    public string? Lyrics { get; set; }
    [MaxLength(12)] public string Status { get; set; } = TrackStatus.Draft;
    public int PlaysCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ArtistProfile Artist { get; set; } = null!;
    public Album? Album { get; set; }
    public List<TrackGenre> TrackGenres { get; set; } = new();
    public List<TrackLike> Likes { get; set; } = new();
    public List<Comment> Comments { get; set; } = new();
}

public class Genre
{
    public int Id { get; set; }
    [MaxLength(40)] public string Name { get; set; } = "";
    [MaxLength(40)] public string Slug { get; set; } = "";
    [MaxLength(7)] public string Color { get; set; } = "#FFE500";

    public List<TrackGenre> TrackGenres { get; set; } = new();
}

public class TrackGenre
{
    public int TrackId { get; set; }
    public int GenreId { get; set; }

    public Track Track { get; set; } = null!;
    public Genre Genre { get; set; } = null!;
}

// ---------- 2.4 Library & Engagement ----------

public class Playlist
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    [MaxLength(100)] public string Title { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }
    [MaxLength(255)] public string? CoverUrl { get; set; }
    public bool IsPublic { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Owner { get; set; } = null!;
    public List<PlaylistTrack> PlaylistTracks { get; set; } = new();
}

public class PlaylistTrack
{
    public int PlaylistId { get; set; }
    public int TrackId { get; set; }
    public int Position { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    public Playlist Playlist { get; set; } = null!;
    public Track Track { get; set; } = null!;
}

public class TrackLike
{
    public int UserId { get; set; }
    public int TrackId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Track Track { get; set; } = null!;
}

public class SavedAlbum
{
    public int UserId { get; set; }
    public int AlbumId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Album Album { get; set; } = null!;
}

public class SavedPlaylist
{
    public int UserId { get; set; }
    public int PlaylistId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Playlist Playlist { get; set; } = null!;
}

public class ListeningHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TrackId { get; set; }
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public int SecondsListened { get; set; }

    public User User { get; set; } = null!;
    public Track Track { get; set; } = null!;
}

public class ShareLink
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public int CreatedBy { get; set; }
    [MaxLength(32)] public string Token { get; set; } = "";
    public int ClickCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Track Track { get; set; } = null!;
    public User Creator { get; set; } = null!;
}

public class Comment
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int TrackId { get; set; }
    public int? ParentId { get; set; }
    public string Body { get; set; } = "";
    public bool IsHidden { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Track Track { get; set; } = null!;
    public Comment? Parent { get; set; }
    public List<Comment> Replies { get; set; } = new();
}

// ---------- 2.5 Administration & Contact ----------

public class Report
{
    public int Id { get; set; }
    public int ReporterId { get; set; }
    public int? TrackId { get; set; }
    public int? CommentId { get; set; }
    public int? ReportedUserId { get; set; }
    [MaxLength(300)] public string Reason { get; set; } = "";
    [MaxLength(12)] public string Status { get; set; } = "open"; // open, resolved, dismissed
    public int? HandledBy { get; set; }

    public User Reporter { get; set; } = null!;
    public Track? Track { get; set; }
    public Comment? Comment { get; set; }
    public User? ReportedUser { get; set; }
    public User? Handler { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public int AdminId { get; set; }
    [MaxLength(60)] public string Action { get; set; } = "";
    [MaxLength(120)] public string Target { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Admin { get; set; } = null!;
}

public class ContactMessage
{
    public int Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "";
    [MaxLength(120)] public string Email { get; set; } = "";
    [MaxLength(30)] public string Topic { get; set; } = "general"; // general, feedback, bug, collaboration
    public string Message { get; set; } = "";
    [MaxLength(10)] public string Status { get; set; } = "new"; // new, read, replied
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
