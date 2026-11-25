using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Wavelength.Web.Data;
using Wavelength.Web.Services;

namespace Wavelength.Web.Models
{
    // View models for the public pages (report section 3.1).

    public static class PublicHelpers
    {
        public static HashSet<int> LikedSet(AppDbContext db, int? uid, IEnumerable<int> trackIds)
        {
            if (uid == null) return new HashSet<int>();
            var ids = trackIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            return new HashSet<int>(db.TrackLikes.Where(l => l.UserId == uid && ids.Contains(l.TrackId)).Select(l => l.TrackId).ToList());
        }

        /// <summary>Marks the liked tracks of the user and returns the same list.</summary>
        public static List<TrackItem> WithLikes(this List<TrackItem> items, AppDbContext db, int? uid)
        {
            items.MarkLiked(LikedSet(db, uid, items.Select(i => i.Id)));
            return items;
        }

        public static string TotalDuration(IEnumerable<TrackItem> tracks)
        {
            var s = tracks.Sum(t => t.DurationSec);
            return s >= 3600 ? (s / 3600) + " hr " + (s % 3600 / 60) + " min" : (s / 60) + " min " + (s % 60) + " sec";
        }

        public static string Ids(IEnumerable<TrackItem> tracks)
        {
            return string.Join(",", tracks.Select(t => t.Id));
        }
    }

    public class GenreItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Color { get; set; }
        public int TrackCount { get; set; }
    }

    public class HomeVm
    {
        public HomeVm() { Trending = new List<TrackItem>(); Genres = new List<string>(); }
        public List<TrackItem> Trending { get; set; }
        public List<string> Genres { get; set; }
        public int Listeners { get; set; }
        public int Tracks { get; set; }
        public long Plays { get; set; }
        public int Countries { get; set; }
        public int SavesToday { get; set; }
    }

    public class ContactForm
    {
        public ContactForm() { Topic = "general"; }

        [Required(ErrorMessage = "Please tell us your name."), StringLength(80, MinimumLength = 2, ErrorMessage = "Please tell us your name.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Enter a valid email address."), EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(120, ErrorMessage = "Email can be at most 120 characters.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Pick a topic.")]
        public string Topic { get; set; }

        [Required(ErrorMessage = "Message needs at least 10 characters."), StringLength(500, MinimumLength = 10, ErrorMessage = "Message needs 10 to 500 characters.")]
        public string Message { get; set; }

        public static readonly Tuple<string, string>[] Topics =
        {
            Tuple.Create("general", "General question"), Tuple.Create("feedback", "Feedback"),
            Tuple.Create("bug", "Report a bug"), Tuple.Create("collaboration", "Collaboration"),
        };
    }

    public class ExploreVm
    {
        public ExploreVm()
        {
            Genres = new List<GenreItem>(); NewReleases = new List<TrackItem>(); ForYou = new List<TrackItem>();
            Artists = new List<ArtistItem>(); Albums = new List<AlbumItem>(); Playlists = new List<PlaylistItem>();
        }
        public List<GenreItem> Genres { get; set; }
        public List<TrackItem> NewReleases { get; set; }
        public List<TrackItem> ForYou { get; set; }
        public bool Personal { get; set; }
        public List<ArtistItem> Artists { get; set; }
        public List<AlbumItem> Albums { get; set; }
        public List<PlaylistItem> Playlists { get; set; }
    }

    public class SearchVm
    {
        public SearchVm()
        {
            Q = ""; Type = "all"; Sort = "popular";
            Genres = new List<GenreItem>(); Tracks = new List<TrackItem>(); Artists = new List<ArtistItem>();
            Albums = new List<AlbumItem>(); Playlists = new List<PlaylistItem>();
        }
        public string Q { get; set; }
        public string Type { get; set; }
        public string Genre { get; set; }
        public string Sort { get; set; }
        public List<GenreItem> Genres { get; set; }
        public List<TrackItem> Tracks { get; set; }
        public List<ArtistItem> Artists { get; set; }
        public List<AlbumItem> Albums { get; set; }
        public List<PlaylistItem> Playlists { get; set; }
        public int Total { get { return Tracks.Count + Artists.Count + Albums.Count + Playlists.Count; } }
        public bool HasFilter { get { return Q != "" || Genre != null; } }

        public string Url(string type = null, string genre = null, string sort = null, bool clearGenre = false)
        {
            var p = new List<string>();
            if (!string.IsNullOrEmpty(Q)) p.Add("q=" + Uri.EscapeDataString(Q));
            var t = type ?? Type; if (t != "all") p.Add("type=" + t);
            var g = clearGenre ? null : genre ?? Genre; if (!string.IsNullOrEmpty(g)) p.Add("genre=" + g);
            var s = sort ?? Sort; if (s != "popular") p.Add("sort=" + s);
            return "/search" + (p.Count > 0 ? "?" + string.Join("&", p) : "");
        }

        public static readonly Tuple<string, string>[] Types =
        {
            Tuple.Create("all", "All"), Tuple.Create("tracks", "Tracks"), Tuple.Create("artists", "Artists"),
            Tuple.Create("albums", "Albums"), Tuple.Create("playlists", "Playlists"),
        };

        public static readonly Tuple<string, string>[] Sorts =
        {
            Tuple.Create("popular", "Popular"), Tuple.Create("new", "Newest"), Tuple.Create("az", "A–Z"),
        };
    }

    public class ChartRow
    {
        public int Rank { get; set; }
        public int? PrevRank { get; set; }
        public int Plays { get; set; }
        public string Movement { get { return PrevRank == null ? "new" : PrevRank > Rank ? "up" : PrevRank < Rank ? "down" : "same"; } }
        public int Delta { get { return PrevRank == null ? 0 : Math.Abs(PrevRank.Value - Rank); } }
    }

    public class ChartTrack : ChartRow { public TrackItem Track { get; set; } }
    public class ChartArtist : ChartRow { public ArtistItem Artist { get; set; } public int Listeners { get; set; } }

    public class ChartsVm
    {
        public ChartsVm() { Tracks = new List<ChartTrack>(); Artists = new List<ChartArtist>(); }
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<ChartTrack> Tracks { get; set; }
        public List<ChartArtist> Artists { get; set; }
        public int TotalPlays { get; set; }
        public int Listeners { get; set; }
    }

    public class GenrePageVm
    {
        public GenrePageVm() { Sort = "popular"; Tracks = new List<TrackItem>(); Artists = new List<ArtistItem>(); Others = new List<GenreItem>(); }
        public GenreItem Genre { get; set; }
        public string Sort { get; set; }
        public List<TrackItem> Tracks { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public int Offset { get; set; }
        public List<ArtistItem> Artists { get; set; }
        public List<GenreItem> Others { get; set; }
    }

    public class CommentVm
    {
        public CommentVm() { Replies = new List<CommentVm>(); }
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string AvatarUrl { get; set; }
        public string Body { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsArtist { get; set; }
        public List<CommentVm> Replies { get; set; }
    }

    /// <summary>One of the signed-in user's playlists in the "add to playlist" menu.</summary>
    public class PlaylistChoice
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public bool HasTrack { get; set; }
    }

    public class TrackPageVm
    {
        public TrackPageVm()
        {
            Genres = new List<GenreItem>(); Comments = new List<CommentVm>(); MoreFromArtist = new List<TrackItem>(); MyPlaylists = new List<PlaylistChoice>();
        }
        public TrackItem Track { get; set; }
        public string Lyrics { get; set; }
        public int ArtistUserId { get; set; }
        public string ArtistAvatar { get; set; }
        public List<GenreItem> Genres { get; set; }
        public int Likes { get; set; }
        public int CommentCount { get; set; }
        public List<CommentVm> Comments { get; set; }
        public List<TrackItem> MoreFromArtist { get; set; }
        public List<PlaylistChoice> MyPlaylists { get; set; }
        public string ShareUrl { get; set; }
        public int ShareClicks { get; set; }
        public bool IsOwner { get; set; }
    }

    public class ArtistPageVm
    {
        public ArtistPageVm() { TopTracks = new List<TrackItem>(); Albums = new List<AlbumItem>(); Similar = new List<ArtistItem>(); }
        public ArtistProfile Artist { get; set; }
        public string Username { get; set; }
        public string Bio { get; set; }
        public string AvatarUrl { get; set; }
        public int Followers { get; set; }
        public long TotalPlays { get; set; }
        public int MonthlyListeners { get; set; }
        public int TrackCount { get; set; }
        public bool Following { get; set; }
        public bool IsSelf { get; set; }
        public List<TrackItem> TopTracks { get; set; }
        public List<AlbumItem> Albums { get; set; }
        public List<ArtistItem> Similar { get; set; }
    }

    public class AlbumPageVm
    {
        public AlbumPageVm() { Tracks = new List<TrackItem>(); More = new List<AlbumItem>(); }
        public AlbumItem Album { get; set; }
        public bool ArtistVerified { get; set; }
        public List<TrackItem> Tracks { get; set; }
        public bool Saved { get; set; }
        public int Saves { get; set; }
        public List<AlbumItem> More { get; set; }
    }

    public class PlaylistPageVm
    {
        public PlaylistPageVm() { Tracks = new List<TrackItem>(); }
        public PlaylistItem Playlist { get; set; }
        public int OwnerId { get; set; }
        public string OwnerAvatar { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<TrackItem> Tracks { get; set; }
        public bool IsOwner { get; set; }
        public bool Saved { get; set; }
        public int Saves { get; set; }
    }

    public class UserPageVm
    {
        public UserPageVm() { Playlists = new List<PlaylistItem>(); FollowsArtists = new List<ArtistItem>(); }
        public User Profile { get; set; }
        public int? ArtistId { get; set; }
        public int Followers { get; set; }
        public int Following { get; set; }
        public int LikedCount { get; set; }
        public bool IsFollowing { get; set; }
        public bool IsSelf { get; set; }
        public List<PlaylistItem> Playlists { get; set; }
        public int PrivatePlaylists { get; set; }
        public List<ArtistItem> FollowsArtists { get; set; }
    }

    // ---------- models of the small shared partials (Views/Shared/_Pub*.cshtml) ----------

    public class PubSectionHeadVm
    {
        public PubSectionHeadVm(string eyebrow, string title, string link = null, string linkText = null)
        {
            Eyebrow = eyebrow; Title = title; Link = link; LinkText = linkText;
        }
        public string Eyebrow { get; set; }
        public string Title { get; set; }
        public string Link { get; set; }
        public string LinkText { get; set; }
    }

    public class PubFollowVm
    {
        public PubFollowVm(int userId, bool following, string returnUrl, string extra = "")
        {
            UserId = userId; Following = following; ReturnUrl = returnUrl; Extra = extra;
        }
        public int UserId { get; set; }
        public bool Following { get; set; }
        public string ReturnUrl { get; set; }
        public string Extra { get; set; }
    }

    /// <summary>Field is trackId, commentId or reportedUserId.</summary>
    public class PubReportVm
    {
        public PubReportVm(string field, int id, string label, string returnUrl)
        {
            Field = field; Id = id; Label = label; ReturnUrl = returnUrl;
        }
        public string Field { get; set; }
        public int Id { get; set; }
        public string Label { get; set; }
        public string ReturnUrl { get; set; }
    }

    public class PubCommentVm
    {
        public PubCommentVm(CommentVm comment, string returnUrl) { Comment = comment; ReturnUrl = returnUrl; }
        public CommentVm Comment { get; set; }
        public string ReturnUrl { get; set; }
    }
}
